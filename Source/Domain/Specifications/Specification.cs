using System.Linq.Expressions;

namespace Domain.Specifications;

public abstract class Specification<T>
{
    public abstract Expression<Func<T, bool>> ToExpression();

    public bool IsSatisfiedBy(T candidate) => ToExpression().Compile()(candidate);

    public Specification<T> And(Specification<T> other) => new AndSpecification<T>(this, other);

    public Specification<T> Or(Specification<T> other) => new OrSpecification<T>(this, other);

    public Specification<T> Not() => new NotSpecification<T>(this);
}

public sealed class AllSpecification<T> : Specification<T>
{
    public override Expression<Func<T, bool>> ToExpression() => _ => true;
}

public sealed class AndSpecification<T>(Specification<T> left, Specification<T> right) : Specification<T>
{
    public override Expression<Func<T, bool>> ToExpression()
    {
        var parameter = Expression.Parameter(typeof(T));

        var body = Expression.AndAlso(
            ExpressionRebinder.Rebind(left.ToExpression(), parameter),
            ExpressionRebinder.Rebind(right.ToExpression(), parameter));

        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }
}

public sealed class OrSpecification<T>(Specification<T> left, Specification<T> right) : Specification<T>
{
    public override Expression<Func<T, bool>> ToExpression()
    {
        var parameter = Expression.Parameter(typeof(T));

        var body = Expression.OrElse(
            ExpressionRebinder.Rebind(left.ToExpression(), parameter),
            ExpressionRebinder.Rebind(right.ToExpression(), parameter));

        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }
}

public sealed class NotSpecification<T>(Specification<T> inner) : Specification<T>
{
    public override Expression<Func<T, bool>> ToExpression()
    {
        var parameter = Expression.Parameter(typeof(T));
        var body = Expression.Not(ExpressionRebinder.Rebind(inner.ToExpression(), parameter));

        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }
}
