using System.Linq.Expressions;
using Domain.Entities;

namespace Domain.Specifications;

public sealed class ProductBySkuSpecification(string sku) : Specification<ProductEntity>
{
    public override Expression<Func<ProductEntity, bool>> ToExpression() =>
        product => product.Sku == sku;
}

public sealed class ActiveProductSpecification : Specification<ProductEntity>
{
    public override Expression<Func<ProductEntity, bool>> ToExpression() =>
        product => product.IsActive;
}

public sealed class ProductSearchSpecification(string term) : Specification<ProductEntity>
{
    public override Expression<Func<ProductEntity, bool>> ToExpression() =>
        product => product.Sku.Contains(term) || product.Name.Contains(term);
}

public sealed class ProductPricedAtLeastSpecification(decimal minimum) : Specification<ProductEntity>
{
    public override Expression<Func<ProductEntity, bool>> ToExpression() =>
        product => product.Price.Amount >= minimum;
}
