/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;


namespace MTGOSDK.SourceGenerators;

/// <summary>
/// Source generator that analyzes DLRWrapper-derived classes and generates
/// a registry of remote access paths for batch serialization.
/// </summary>
[Generator]
public class RemoteAccessPathGenerator : ISourceGenerator
{
  public void Initialize(GeneratorInitializationContext context)
  {
    context.RegisterForSyntaxNotifications(() => new DLRWrapperReceiver());
  }

  public void Execute(GeneratorExecutionContext context)
  {
    if (context.SyntaxReceiver is not DLRWrapperReceiver receiver)
      return;

    var compilation = context.Compilation;
    var allClassPaths = new List<(string FullTypeName, List<(string PropName, string Path)> Paths)>();
    var debugInfo = new List<string>();
    
    debugInfo.Add($"// Candidate classes found: {receiver.CandidateClasses.Count}");

    foreach (var classDecl in receiver.CandidateClasses)
    {
      var semanticModel = compilation.GetSemanticModel(classDecl.SyntaxTree);
      var classSymbol = semanticModel.GetDeclaredSymbol(classDecl);
      
      if (classSymbol == null)
      {
        debugInfo.Add($"// Class {classDecl.Identifier.Text}: no symbol");
        continue;
      }
      
      if (!IsDLRWrapperDerived(classSymbol))
      {
        // Don't log non-DLRWrapper classes to avoid noise
        continue;
      }
      
      debugInfo.Add($"// DLRWrapper class found: {classSymbol.ToDisplayString()}");

      // Get the interface type from `type` property or Bind<T>() calls
      var interfaceType = GetBindingInterfaceType(classDecl, semanticModel);
      debugInfo.Add($"//   Interface type: {interfaceType?.ToDisplayString() ?? "null"}");

      var paths = new List<(string PropName, string Path)>();

      // Get all properties including inherited ones using semantic model
      var allProperties = classSymbol.GetMembers()
        .OfType<IPropertySymbol>()
        .Where(p => !p.IsStatic); // Skip static properties

      foreach (var propSymbol in allProperties)
      {
        // Get the syntax declaration for this property
        var syntaxRef = propSymbol.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxRef == null) continue;

        var propDecl = syntaxRef.GetSyntax() as PropertyDeclarationSyntax;
        if (propDecl == null) continue;

        // Get the semantic model for the syntax tree where this property is declared
        // (it might be in a different file if inherited)
        var propSemanticModel = compilation.GetSemanticModel(propDecl.SyntaxTree);
        
        // Only debug Card class properties
        bool isCardClass = classSymbol.Name == "Card";
        
        var pathInfo = AnalyzeProperty(propDecl, propSemanticModel, interfaceType, isCardClass ? debugInfo : null);
        if (pathInfo.HasValue)
        {
          paths.Add((propDecl.Identifier.Text, pathInfo.Value.Path));
          debugInfo.Add($"//   Property {propDecl.Identifier.Text}: path = {pathInfo.Value.Path}");
        }
      }

      if (paths.Count == 0)
      {
        debugInfo.Add($"//   No paths found for {classSymbol.Name}");
        continue;
      }

      var fullTypeName = classSymbol.ToDisplayString();
      allClassPaths.Add((fullTypeName, paths));
    }

    // Always generate the registry file (even if empty) for debugging
    var source = GenerateRegistry(allClassPaths, debugInfo);
    context.AddSource("RemoteAccessPathRegistry.g.cs", SourceText.From(source, Encoding.UTF8));
  }

  private bool IsDLRWrapperDerived(INamedTypeSymbol classSymbol)
  {
    var baseType = classSymbol.BaseType;
    while (baseType != null)
    {
      // Match both DLRWrapper and DLRWrapper<T> (which is named DLRWrapper`1)
      var name = baseType.MetadataName ?? baseType.Name;
      if (name == "DLRWrapper" || name.StartsWith("DLRWrapper`"))
        return true;
      baseType = baseType.BaseType;
    }
    return false;
  }

  private ITypeSymbol? GetBindingInterfaceType(
    ClassDeclarationSyntax classDecl,
    SemanticModel semanticModel)
  {
    // Look for `type` property: `internal override Type type => typeof(IFoo);`
    foreach (var member in classDecl.Members)
    {
      if (member is not PropertyDeclarationSyntax prop) continue;
      if (prop.Identifier.Text != "type") continue;
      
      // Check arrow expression: typeof(IFoo)
      if (prop.ExpressionBody?.Expression is TypeOfExpressionSyntax typeOf)
      {
        var typeInfo = semanticModel.GetTypeInfo(typeOf.Type);
        if (typeInfo.Type != null)
          return typeInfo.Type;
      }
    }

    // Fallback: look for `obj` property with Bind<T>() call
    foreach (var member in classDecl.Members)
    {
      if (member is not PropertyDeclarationSyntax prop) continue;
      if (prop.Identifier.Text != "obj") continue;
      
      var expr = prop.ExpressionBody?.Expression;
      if (expr == null) continue;

      // Look for Bind<T>(...) invocation
      var invocation = FindBindInvocation(expr);
      if (invocation != null)
      {
        var methodSymbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (methodSymbol?.TypeArguments.Length > 0)
          return methodSymbol.TypeArguments[0];
      }
    }

    return null;
  }

  private InvocationExpressionSyntax? FindBindInvocation(ExpressionSyntax expr)
  {
    // Direct Bind<T>(...) call
    if (expr is InvocationExpressionSyntax inv)
    {
      var name = inv.Expression switch
      {
        GenericNameSyntax gns => gns.Identifier.Text,
        MemberAccessExpressionSyntax mae => (mae.Name as GenericNameSyntax)?.Identifier.Text,
        _ => null
      };
      if (name == "Bind") return inv;

      // Check nested invocations
      foreach (var arg in inv.ArgumentList.Arguments)
      {
        var found = FindBindInvocation(arg.Expression);
        if (found != null) return found;
      }
    }

    return null;
  }

  private (string PropertyName, string Path, bool UsesUnbind, bool IsPrimitive)? AnalyzeProperty(
    PropertyDeclarationSyntax propDecl,
    SemanticModel semanticModel,
    ITypeSymbol? interfaceType,
    List<string>? debugInfo = null)
  {
    // Skip properties with [NonSerializable] or [RuntimeInternal]
    if (propDecl.AttributeLists
        .SelectMany(al => al.Attributes)
        .Any(a => a.Name.ToString() == "NonSerializable" || a.Name.ToString() == "RuntimeInternal"))
    {
      debugInfo?.Add($"//     {propDecl.Identifier.Text}: skipped (attribute)");
      return null;
    }

    var expr = GetPropertyExpression(propDecl);
    if (expr == null)
    {
      debugInfo?.Add($"//     {propDecl.Identifier.Text}: no expression body");
      return null;
    }
    
    debugInfo?.Add($"//     {propDecl.Identifier.Text}: expr type = {expr.GetType().Name}, text = {expr.ToString().Replace("\r", "").Replace("\n", " ").Substring(0, Math.Min(50, expr.ToString().Length))}");

    // Analyze for @base.X or Unbind(...).X patterns
    var pathResult = ExtractRemotePath(expr, debugInfo, propDecl.Identifier.Text);
    if (pathResult == null)
    {
      debugInfo?.Add($"//     {propDecl.Identifier.Text}: no path extracted");
      return null;
    }

    var (path, usesUnbind) = pathResult.Value;
    
    // Determine if primitive based on interface type
    bool isPrimitive = false;
    if (interfaceType != null && !usesUnbind)
    {
      var ifaceProp = interfaceType.GetMembers(path.Split('.')[0])
        .OfType<IPropertySymbol>()
        .FirstOrDefault();
      if (ifaceProp != null)
      {
        isPrimitive = IsPrimitiveType(ifaceProp.Type);
      }
    }

    return (propDecl.Identifier.Text, path, usesUnbind, isPrimitive);
  }

  private ExpressionSyntax? GetPropertyExpression(PropertyDeclarationSyntax propDecl)
  {
    // Arrow expression: => ...
    if (propDecl.ExpressionBody != null)
      return propDecl.ExpressionBody.Expression;

    // Getter body: get { return ...; } or get => ...
    var getter = propDecl.AccessorList?.Accessors
      .FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
    if (getter?.ExpressionBody != null)
      return getter.ExpressionBody.Expression;

    // Full body with return statement
    if (getter?.Body != null)
    {
      var returnStmt = getter.Body.Statements
        .OfType<ReturnStatementSyntax>()
        .FirstOrDefault();
      return returnStmt?.Expression;
    }

    return null;
  }

  private (string Path, bool UsesUnbind)? ExtractRemotePath(ExpressionSyntax expr, List<string>? debugInfo = null, string? propName = null)
  {
    // Handle: field ??= @base.X
    if (expr is AssignmentExpressionSyntax assignment)
    {
      debugInfo?.Add($"//       {propName}: is AssignmentExpressionSyntax, kind = {assignment.Kind()}");
      if (assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression))
      {
        debugInfo?.Add($"//       {propName}: coalesce RHS type = {assignment.Right.GetType().Name}, text = {assignment.Right.ToString().Replace("\r", "").Replace("\n", " ").Substring(0, Math.Min(40, assignment.Right.ToString().Replace("\r", "").Replace("\n", " ").Length))}");
        return ExtractRemotePath(assignment.Right, debugInfo, propName);
      }
    }

    // Handle: Try(() => @base.X, ...) or Try<T>(() => @base.X)
    if (expr is InvocationExpressionSyntax inv)
    {
      var methodName = inv.Expression switch
      {
        IdentifierNameSyntax ins => ins.Identifier.Text,
        GenericNameSyntax gns => gns.Identifier.Text,
        _ => null
      };
      
      if (methodName == "Try" && inv.ArgumentList.Arguments.Count > 0)
      {
        var firstArg = inv.ArgumentList.Arguments[0].Expression;
        ExpressionSyntax? lambdaBody = null;
        
        if (firstArg is ParenthesizedLambdaExpressionSyntax lambda)
        {
          lambdaBody = lambda.ExpressionBody 
            ?? lambda.Block?.Statements.FirstOrDefault()?.DescendantNodes().OfType<ExpressionSyntax>().FirstOrDefault();
        }
        else if (firstArg is SimpleLambdaExpressionSyntax simpleLambda)
        {
          lambdaBody = simpleLambda.ExpressionBody ?? simpleLambda.Body as ExpressionSyntax;
        }
        
        if (lambdaBody != null)
          return ExtractRemotePath(lambdaBody);
      }

      // Handle: Cast<T>(Unbind(this).X)
      if (methodName == "Cast" && inv.ArgumentList.Arguments.Count > 0)
      {
        return ExtractRemotePath(inv.ArgumentList.Arguments[0].Expression, debugInfo, propName);
      }

      // Handle: Map<T>(...)
      if (methodName == "Map" && inv.ArgumentList.Arguments.Count > 0)
      {
        return ExtractRemotePath(inv.ArgumentList.Arguments[0].Expression, debugInfo, propName);
      }

      // Handle: Unbind(...) (e.g. inside new Set(Unbind(@base.CardSet)))
      if (methodName == "Unbind" && inv.ArgumentList.Arguments.Count > 0)
      {
        return ExtractRemotePath(inv.ArgumentList.Arguments[0].Expression, debugInfo, propName);
      }
      
      // Handle: Unbind(this).X or Unbind(@base).X
      if (inv.Expression is MemberAccessExpressionSyntax mae)
      {
        // Handle method chaining: Unbind(this).Prop.ToString().Split(...)
        // Special case: if this is .ToString(), preserve it in the path
        // CHECK THIS FIRST before checking for Unbind, otherwise we'll return early!
        if (methodName == "ToString" && mae.Expression != null)
        {
          debugInfo?.Add($"//       {propName}: detected .ToString() call");
          var innerPath = ExtractRemotePath(mae.Expression, debugInfo, propName);
          if (innerPath != null)
          {
            // Append .ToString() to the path and return immediately
            debugInfo?.Add($"//       {propName}: appending .ToString() to path '{innerPath.Value.Path}'");
            return (innerPath.Value.Path + ".ToString()", innerPath.Value.UsesUnbind);
          }
        }

        var innerInvoke = mae.Expression as InvocationExpressionSyntax;
        if (innerInvoke != null)
        {
          var innerName = innerInvoke.Expression switch
          {
            IdentifierNameSyntax ins => ins.Identifier.Text,
            _ => null
          };
          if (innerName == "Unbind")
          {
            // Path is the member access chain after Unbind()
            return (GetMemberAccessPath(mae), true);
          }
        }

        // For other method calls, recurse on the object expression
        if (mae.Expression == null) return null;
        debugInfo?.Add($"//       {propName}: checking method chain on {methodName}, expr = {mae.Expression.ToString().Replace("\r", "").Replace("\n", " ").Substring(0, Math.Min(30, mae.Expression.ToString().Length))}...");
        var extractedPath = ExtractRemotePath(mae.Expression, debugInfo, propName);
        if (extractedPath != null) return extractedPath;
      }
    }

    // Handle: new Set(Unbind(...)) or new(...)
    if (expr is ObjectCreationExpressionSyntax objCreation && objCreation.ArgumentList?.Arguments.Count > 0)
    {
       // Check first argument for remote path
       return ExtractRemotePath(objCreation.ArgumentList.Arguments[0].Expression, debugInfo, propName);
    }
    if (expr is ImplicitObjectCreationExpressionSyntax implicitCreation && implicitCreation.ArgumentList?.Arguments.Count > 0)
    {
       // Check first argument for remote path
       return ExtractRemotePath(implicitCreation.ArgumentList.Arguments[0].Expression, debugInfo, propName);
    }

    // Handle: @base.X.Y (member access chain)
    if (expr is MemberAccessExpressionSyntax memberAccess)
    {
      // Check if starts with @base (Roslyn stores as "base" without the @ prefix)
      var root = GetRootExpression(memberAccess);
      debugInfo?.Add($"//       {propName}: is MemberAccess, root type = {root.GetType().Name}, root text = {root.ToString().Replace("\r", "").Replace("\n", " ")}, ValueText = {(root as IdentifierNameSyntax)?.Identifier.ValueText}");
      if (root is IdentifierNameSyntax id && id.Identifier.ValueText == "base")
      {
        // Extract path excluding @base
        return (GetMemberAccessPathExcludingBase(memberAccess), false);
      }
      
      // Check Unbind(this).X
      if (root is InvocationExpressionSyntax invRoot)
      {
        var invName = invRoot.Expression switch
        {
          IdentifierNameSyntax ins => ins.Identifier.Text,
          _ => null
        };
        if (invName == "Unbind")
        {
          return (GetMemberAccessPath(memberAccess), true);
        }
      }
    }
    
    debugInfo?.Add($"//       {propName}: no match found, expr type = {expr.GetType().Name}");

    return null;
  }

  private ExpressionSyntax GetRootExpression(MemberAccessExpressionSyntax mae)
  {
    ExpressionSyntax current = mae;
    while (current is MemberAccessExpressionSyntax inner)
      current = inner.Expression;
    return current;
  }

  private string GetMemberAccessPath(MemberAccessExpressionSyntax mae)
  {
    var parts = new List<string>();
    ExpressionSyntax current = mae;

    while (current is MemberAccessExpressionSyntax inner)
    {
      parts.Insert(0, inner.Name.Identifier.Text);
      current = inner.Expression;
    }

    return string.Join(".", parts);
  }

  private string GetMemberAccessPathExcludingBase(MemberAccessExpressionSyntax mae)
  {
    var parts = new List<string>();
    ExpressionSyntax current = mae;

    while (current is MemberAccessExpressionSyntax inner)
    {
      parts.Insert(0, inner.Name.Identifier.Text);
      current = inner.Expression;
    }

    // Skip @base (the last item we stopped at)
    return string.Join(".", parts);
  }

  private bool IsPrimitiveType(ITypeSymbol type)
  {
    var typeName = type.ToDisplayString();
    return type.TypeKind == TypeKind.Enum ||
           typeName == "string" || typeName == "System.String" ||
           typeName == "int" || typeName == "System.Int32" ||
           typeName == "bool" || typeName == "System.Boolean" ||
           typeName == "long" || typeName == "System.Int64" ||
           typeName == "double" || typeName == "System.Double" ||
           typeName == "float" || typeName == "System.Single" ||
           typeName == "decimal" || typeName == "System.Decimal" ||
           typeName == "System.DateTime" ||
           typeName == "System.TimeSpan" ||
           typeName == "System.Guid";
  }

  private string GenerateRegistry(
    List<(string FullTypeName, List<(string PropName, string Path)> Paths)> allClassPaths,
    List<string>? debugInfo = null)
  {
    var sb = new StringBuilder();
    sb.AppendLine("// <auto-generated/>");
    sb.AppendLine("// Generated by RemoteAccessPathGenerator");
    sb.AppendLine($"// Generated at: {DateTime.Now:O}");
    sb.AppendLine("#nullable enable");
    sb.AppendLine();
    
    // Include debug info as comments
    if (debugInfo != null && debugInfo.Count > 0)
    {
      sb.AppendLine("// === DEBUG INFO ===");
      foreach (var line in debugInfo)
      {
        sb.AppendLine(line);
      }
      sb.AppendLine("// === END DEBUG INFO ===");
      sb.AppendLine();
    }
    
    sb.AppendLine("using System;");
    sb.AppendLine("using System.Linq;");
    sb.AppendLine("using System.Collections.Generic;");
    sb.AppendLine();
    sb.AppendLine("namespace MTGOSDK.Core.Reflection.Serialization;");
    sb.AppendLine();
    sb.AppendLine("/// <summary>");
    sb.AppendLine("/// Auto-generated registry of remote access paths for DLRWrapper classes.");
    sb.AppendLine("/// Used by AccessPathAnalyzer for batch serialization.");
    sb.AppendLine("/// </summary>");
    sb.AppendLine("internal static class RemoteAccessPathRegistry");
    sb.AppendLine("{");
    sb.AppendLine("  private static readonly Dictionary<string, Dictionary<string, string>> _paths = new()");
    sb.AppendLine("  {");
    
    foreach (var (fullTypeName, paths) in allClassPaths)
    {
      sb.AppendLine($"    {{ \"{fullTypeName}\", new Dictionary<string, string> {{");
      foreach (var (propName, path) in paths)
      {
        sb.AppendLine($"      {{ \"{propName}\", \"{path}\" }},");
      }
      sb.AppendLine("    } },");
    }
    
    sb.AppendLine("  };");
    sb.AppendLine();
    sb.AppendLine("  /// <summary>");
    sb.AppendLine("  /// Gets the remote access paths for a DLRWrapper type.");
    sb.AppendLine("  /// </summary>");
    sb.AppendLine("  public static string[] GetPaths(Type wrapperType)");
    sb.AppendLine("  {");
    sb.AppendLine("    var typeName = wrapperType.FullName ?? wrapperType.Name;");
    sb.AppendLine("    return _paths.TryGetValue(typeName, out var map) ? map.Values.ToArray() : Array.Empty<string>();");
    sb.AppendLine("  }");
    sb.AppendLine();
    sb.AppendLine("  /// <summary>");
    sb.AppendLine("  /// Gets the property name to remote path mapping for a DLRWrapper type.");
    sb.AppendLine("  /// </summary>");
    sb.AppendLine("  public static Dictionary<string, string> GetPropertyMap(Type wrapperType)");
    sb.AppendLine("  {");
    sb.AppendLine("    var typeName = wrapperType.FullName ?? wrapperType.Name;");
    sb.AppendLine("    return _paths.TryGetValue(typeName, out var map) ? map : new Dictionary<string, string>();");
    sb.AppendLine("  }");
    sb.AppendLine();
    sb.AppendLine("  /// <summary>");
    sb.AppendLine("  /// Gets the remote access paths for a DLRWrapper type by full name.");
    sb.AppendLine("  /// </summary>");
    sb.AppendLine("  public static string[] GetPaths(string fullTypeName)");
    sb.AppendLine("  {");
    sb.AppendLine("    return _paths.TryGetValue(fullTypeName, out var map) ? map.Values.ToArray() : Array.Empty<string>();");
    sb.AppendLine("  }");
    sb.AppendLine("}");

    return sb.ToString(); 
  }

  /// <summary>
  /// Syntax receiver to collect candidate DLRWrapper-derived classes.
  /// </summary>
  private class DLRWrapperReceiver : ISyntaxReceiver
  {
    public List<ClassDeclarationSyntax> CandidateClasses { get; } = new();

    public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
    {
      if (syntaxNode is ClassDeclarationSyntax classDecl)
      {
        // Quick filter: must have a base type
        if (classDecl.BaseList != null)
        {
          CandidateClasses.Add(classDecl);
        }
      }
    }
  }
}
