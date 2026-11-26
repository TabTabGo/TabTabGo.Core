using System.Linq.Expressions;
using TabTabGo.Core.Api.Controllers;

namespace TabTabGo.Core.Api.Tests;

/// <summary>V11-3: the in-memory scope check only says "outside" when it is certain.</summary>
public class ScopeCheckTests
{
    private sealed class Child
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class Owner
    {
        public string Tenant { get; set; } = "A";

        public Child? Category { get; set; }

        public List<Child>? Children { get; set; }

        public int? Rank { get; set; }
    }

    [Fact]
    public void IsDefinitelyOutside_ScalarScope_DecidesInMemory()
    {
        Assert.True(ScopeCheck.IsDefinitelyOutside<Owner>(o => o.Tenant == "B", new Owner()));
        Assert.False(ScopeCheck.IsDefinitelyOutside<Owner>(o => o.Tenant == "A", new Owner()));
        Assert.False(ScopeCheck.IsDefinitelyOutside<Owner>(null, new Owner()));
        Assert.True(ScopeCheck.IsDefinitelyOutside<Owner>(o => o.Rank == 3, new Owner())); // nullable scalar is still a scalar
    }

    [Fact]
    public void IsDefinitelyOutside_NullCollectionWithAny_IsUnknownNotAnException()
    {
        // Any over null throws ArgumentNullException, which used to escape as a 500
        Assert.False(ScopeCheck.IsDefinitelyOutside<Owner>(o => o.Children!.Any(c => c.Name == "x"), new Owner { Children = null }));
    }

    [Fact]
    public void IsDefinitelyOutside_InitialisedButUnloadedCollection_IsUnknownNotAFalse404()
    {
        // an empty, not loaded, collection evaluates false; the database may say true
        Assert.False(ScopeCheck.IsDefinitelyOutside<Owner>(o => o.Children!.Any(c => c.Name == "x"), new Owner { Children = new List<Child>() }));
    }

    [Fact]
    public void IsDefinitelyOutside_NavigationScope_IsAlwaysLeftToTheDatabase()
    {
        // evaluates false in memory (the loaded navigation differs), but a scope over a navigation is decided by the database
        Assert.False(ScopeCheck.IsDefinitelyOutside<Owner>(o => o.Category!.Name == "Hardware", new Owner { Category = new Child { Name = "Other" } }));
        Assert.False(ScopeCheck.IsDefinitelyOutside<Owner>(o => o.Category != null, new Owner()));
    }

    [Fact]
    public void IsDefinitelyOutside_AnythingThatThrows_IsUnknown()
    {
        Assert.False(ScopeCheck.IsDefinitelyOutside<Owner>(o => 1 / o.Tenant.Length == 1, new Owner { Tenant = string.Empty }));
    }

    [Fact]
    public void IsDefinitelyOutside_CompilesTheScopeOnce()
    {
        Expression<Func<Owner, bool>> scope = o => o.Tenant == "B";

        // the same instance twice gives the same answer from the cached delegate
        Assert.True(ScopeCheck.IsDefinitelyOutside(scope, new Owner()));
        Assert.True(ScopeCheck.IsDefinitelyOutside(scope, new Owner()));
        Assert.False(ScopeCheck.IsDefinitelyOutside(scope, new Owner { Tenant = "B" }));
    }
}
