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
    if (expression.Body is not BinaryExpression binary)
      throw new ArgumentException(
        "Expression must be a binary comparison (e.g., o => o.Foo > 0)", 
        nameof(expression));

    // Get the comparison operator
    var op = binary.NodeType switch
    {
      ExpressionType.Equal => ComparisonOperator.Equal,
      ExpressionType.NotEqual => ComparisonOperator.NotEqual,
      ExpressionType.GreaterThan => ComparisonOperator.GreaterThan,
      ExpressionType.GreaterThanOrEqual => ComparisonOperator.GreaterThanOrEqual,
      ExpressionType.LessThan => ComparisonOperator.LessThan,
      ExpressionType.LessThanOrEqual => ComparisonOperator.LessThanOrEqual,
      _ => throw new ArgumentException($"Unsupported comparison operator: {binary.NodeType}")
    };

    // Extract property name from left side
    string propertyName = ExtractPropertyName(binary.Left);

    // Extract value from right side
    object value = ExtractValue(binary.Right);

    return (propertyName, op, value);
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
    return expression switch
    {
      MemberExpression member => member.Member.Name,
      UnaryExpression { Operand: MemberExpression member } => member.Member.Name, // Handle boxing/conversion
      _ => throw new ArgumentException(
        $"Expected a property access expression, got: {expression.NodeType}")
    };
  }

  /// <summary>
  /// Extracts the constant value from an expression.
  /// </summary>
  private static object ExtractValue(Expression expression)
  {
    return expression switch
    {
      ConstantExpression constant => constant.Value,
      UnaryExpression { Operand: ConstantExpression constant } => constant.Value, // Handle boxing
      MemberExpression member => EvaluateMemberExpression(member), // Captured variable
      _ => throw new ArgumentException(
        $"Expected a constant or captured value, got: {expression.NodeType}")
    };
  }

  /// <summary>
  /// Evaluates a member expression to get the value of a captured variable.
  /// </summary>
  private static object EvaluateMemberExpression(MemberExpression member)
  {
    // Compile and invoke to get the captured value
    var objectMember = Expression.Convert(member, typeof(object));
    var getterLambda = Expression.Lambda<Func<object>>(objectMember);
    return getterLambda.Compile()();
  }
}
