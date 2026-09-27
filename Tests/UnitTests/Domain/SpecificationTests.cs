using System.Linq.Expressions;
using Domain.Specifications;

namespace UnitTests.Domain;

public sealed class SpecificationTests
{
    private sealed record Sample(string Name, int Age, bool IsActive);

    private sealed class NameIs(string name) : Specification<Sample>
    {
        public override Expression<Func<Sample, bool>> ToExpression() =>
            sample => sample.Name == name;
    }

    private sealed class OlderThan(int age) : Specification<Sample>
    {
        public override Expression<Func<Sample, bool>> ToExpression() =>
            sample => sample.Age > age;
    }

    private sealed class IsActive : Specification<Sample>
    {
        public override Expression<Func<Sample, bool>> ToExpression() =>
            sample => sample.IsActive;
    }

    [Fact]
    public void And_CompilesAndMatchesOnlyWhenBothHold()
    {
        var specification = new NameIs("Ada").And(new OlderThan(30));

        specification.IsSatisfiedBy(new Sample("Ada", 36, true)).ShouldBeTrue();
        specification.IsSatisfiedBy(new Sample("Ada", 20, true)).ShouldBeFalse();
        specification.IsSatisfiedBy(new Sample("Grace", 36, true)).ShouldBeFalse();
    }

    [Fact]
    public void Or_CompilesAndMatchesWhenEitherHolds()
    {
        var specification = new NameIs("Ada").Or(new OlderThan(30));

        specification.IsSatisfiedBy(new Sample("Ada", 20, true)).ShouldBeTrue();
        specification.IsSatisfiedBy(new Sample("Grace", 36, true)).ShouldBeTrue();
        specification.IsSatisfiedBy(new Sample("Grace", 20, true)).ShouldBeFalse();
    }

    [Fact]
    public void Not_InvertsTheInnerSpecification()
    {
        var specification = new IsActive().Not();

        specification.IsSatisfiedBy(new Sample("Ada", 36, false)).ShouldBeTrue();
        specification.IsSatisfiedBy(new Sample("Ada", 36, true)).ShouldBeFalse();
    }

    [Fact]
    public void DeeplyNestedComposition_StillCompiles()
    {
        var specification = new NameIs("Ada")
            .And(new OlderThan(18))
            .Or(new IsActive().Not())
            .And(new OlderThan(5));

        var act = () => specification.IsSatisfiedBy(new Sample("Ada", 36, true));

        act.ShouldNotThrow();
    }

    [Fact]
    public void ComposedExpression_IsQueryableByLinq()
    {
        var samples = new[]
        {
            new Sample("Ada", 36, true),
            new Sample("Grace", 45, false),
            new Sample("Alan", 25, true),
        }.AsQueryable();

        var specification = new OlderThan(30).And(new IsActive());

        var matches = samples.Where(specification.ToExpression()).ToArray();

        matches.Length.ShouldBe(1);
        matches[0].Name.ShouldBe("Ada");
    }

    [Fact]
    public void AllSpecification_MatchesEverything()
    {
        var specification = new AllSpecification<Sample>();

        specification.IsSatisfiedBy(new Sample("Anyone", 1, false)).ShouldBeTrue();
    }

    [Fact]
    public void AllSpecification_IsUsableAsAFoldSeed()
    {
        Specification<Sample> specification = new AllSpecification<Sample>();

        specification = specification.And(new OlderThan(18));
        specification = specification.And(new IsActive());

        specification.IsSatisfiedBy(new Sample("Ada", 36, true)).ShouldBeTrue();
        specification.IsSatisfiedBy(new Sample("Alan", 10, true)).ShouldBeFalse();
    }
}
