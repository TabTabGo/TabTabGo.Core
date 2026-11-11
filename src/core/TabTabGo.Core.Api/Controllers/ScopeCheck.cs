using System.Linq.Expressions;

namespace TabTabGo.Core.Api.Controllers;

/// <summary>The in-memory half of the scope check on a patch: a second layer in front of the database check.</summary>
internal static class ScopeCheck
{
    /// <summary>
    /// True only when the scope definitely evaluates to false for the loaded entity. A predicate that cannot be evaluated in
    /// memory (a navigation that is not loaded gives a <see cref="NullReferenceException"/>) is "unknown" and returns false: the
    /// database check after the save decides.
    /// </summary>
    /// <param name="scope">The scope; null means none</param>
    /// <param name="entity">The loaded and patched entity</param>
    internal static bool IsDefinitelyOutside<TEntity>(Expression<Func<TEntity, bool>>? scope, TEntity entity)
    {
        if (scope == null)
        {
            return false;
        }

        try
        {
            return !scope.Compile()(entity);
        }
        catch (Exception ex) when (ex is NullReferenceException or InvalidOperationException)
        {
            return false;
        }
    }
}
