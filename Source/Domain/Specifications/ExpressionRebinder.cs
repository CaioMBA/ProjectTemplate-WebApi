using System.Linq.Expressions;

namespace Domain.Specifications;

internal static class ExpressionRebinder
{
    public static Expression Rebind(LambdaExpression expression, ParameterExpression parameter) =>
        new ParameterReplacer(expression.Parameters[0], parameter).Visit(expression.Body);

    private sealed class ParameterReplacer(ParameterExpression source, ParameterExpression target)
        : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == source ? target : base.VisitParameter(node);
    }
}
