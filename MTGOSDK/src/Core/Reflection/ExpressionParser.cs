/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Linq.Expressions;

using MTGOSDK.Core.Remoting.Interop;


namespace MTGOSDK.Core.Reflection;

/// <summary>
/// Parses simple lambda expressions into serializable components for remote execution.
/// </summary>
/// <remarks>
/// This parser extracts property names, comparison operators, and values from
/// expressions like <c>o => o.Foo > 5</c> so they can be passed to remote
/// helper methods without marshaling the actual lambda.
/// </remarks>
public static class ExpressionParser
{
  /// <summary>
  /// Parses a simple binary comparison expression.
  /// </summary>
  /// <typeparam name="T">The source type.</typeparam>
  /// <param name="expression">An expression like <c>o => o.Foo > 5</c></param>
  /// <returns>A tuple of (propertyName, operator, value).</returns>
  /// <exception cref="ArgumentException">Thrown if the expression is not a supported format.</exception>
  public static (string PropertyName, ComparisonOperator Op, object Value) ParsePredicate<T>(
    Expression<Func<T, bool>> expression)
  {
    if (StripConversions(expression.Body) is not BinaryExpression binary)
      throw new ArgumentException(
        "Expression must be a binary comparison (e.g., o => o.Foo > 0)", 
        nameof(expression));

    Expression left = StripConversions(binary.Left);
    Expression right = StripConversions(binary.Right);
    var op = ToComparisonOperator(binary.NodeType);

    if (TryExtractPropertyName(left, out string propertyName))
    {
      return (propertyName, op, ExtractValue(right));
    }

    if (TryExtractPropertyName(right, out propertyName))
    {
      return (propertyName, Invert(op), ExtractValue(left));
    }

    throw new ArgumentException(
      "Expression must compare a property access to a constant or captured value.",
      nameof(expression));
  }

  /// <summary>
  /// Parses a key selector expression for sorting.
  /// </summary>
  /// <typeparam name="T">The source type.</typeparam>
  /// <typeparam name="TKey">The key type.</typeparam>
  /// <param name="expression">An expression like <c>o => o.StartTime</c></param>
  /// <returns>The property name to sort by.</returns>
  public static string ParseKeySelector<T, TKey>(Expression<Func<T, TKey>> expression)
  {
    return ExtractPropertyName(expression.Body);
  }

  /// <summary>
  /// Extracts the property name from a member expression.
  /// </summary>
  private static string ExtractPropertyName(Expression expression)
  {
    expression = StripConversions(expression);

    if (TryExtractPropertyName(expression, out string propertyName))
      return propertyName;

    throw new ArgumentException(
      $"Expected a property access expression, got: {expression.NodeType}");
  }

  /// <summary>
  /// Extracts the constant value from an expression.
  /// </summary>
  private static object ExtractValue(Expression expression)
  {
    expression = StripConversions(expression);

    switch (expression)
    {
      case ConstantExpression constant:
        return constant.Value;
      case MemberExpression member:
        return EvaluateExpression(member);
      default:
        return EvaluateExpression(expression);
    }
  }

  /// <summary>
  /// Evaluates an expression to get the value of a constant expression.
  /// </summary>
  private static object EvaluateExpression(Expression expression)
  {
    try
    {
      var objectValue = Expression.Convert(expression, typeof(object));
      var getterLambda = Expression.Lambda<Func<object>>(objectValue);
      return getterLambda.Compile()();
    }
    catch (Exception ex)
    {
      throw new ArgumentException(
        $"Expected a constant or captured value, got: {expression.NodeType}",
        ex);
    }
  }

  private static Expression StripConversions(Expression expression)
  {
    while (expression is UnaryExpression unary &&
           (unary.NodeType == ExpressionType.Convert ||
            unary.NodeType == ExpressionType.ConvertChecked ||
            unary.NodeType == ExpressionType.Quote))
    {
      expression = unary.Operand;
    }

    return expression;
  }

  private static bool TryExtractPropertyName(
    Expression expression,
    out string propertyName)
  {
    Stack<string> members = new();
    Expression current = StripConversions(expression);

    while (current is MemberExpression member)
    {
      members.Push(member.Member.Name);

      if (member.Expression == null)
      {
        propertyName = string.Empty;
        return false;
      }

      current = StripConversions(member.Expression);
    }

    if (current is ParameterExpression && members.Count > 0)
    {
      propertyName = string.Join(".", members);
      return true;
    }

    propertyName = string.Empty;
    return false;
  }

  private static ComparisonOperator ToComparisonOperator(ExpressionType type)
  {
    return type switch
    {
      ExpressionType.Equal => ComparisonOperator.Equal,
      ExpressionType.NotEqual => ComparisonOperator.NotEqual,
      ExpressionType.GreaterThan => ComparisonOperator.GreaterThan,
      ExpressionType.GreaterThanOrEqual => ComparisonOperator.GreaterThanOrEqual,
      ExpressionType.LessThan => ComparisonOperator.LessThan,
      ExpressionType.LessThanOrEqual => ComparisonOperator.LessThanOrEqual,
      _ => throw new ArgumentException($"Unsupported comparison operator: {type}")
    };
  }

  private static ComparisonOperator Invert(ComparisonOperator op)
  {
    return op switch
    {
      ComparisonOperator.Equal => ComparisonOperator.Equal,
      ComparisonOperator.NotEqual => ComparisonOperator.NotEqual,
      ComparisonOperator.GreaterThan => ComparisonOperator.LessThan,
      ComparisonOperator.GreaterThanOrEqual => ComparisonOperator.LessThanOrEqual,
      ComparisonOperator.LessThan => ComparisonOperator.GreaterThan,
      ComparisonOperator.LessThanOrEqual => ComparisonOperator.GreaterThanOrEqual,
      _ => throw new ArgumentException($"Unsupported comparison operator: {op}")
    };
  }
}
