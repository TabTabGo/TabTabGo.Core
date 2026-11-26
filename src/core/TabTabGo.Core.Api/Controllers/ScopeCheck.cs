using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace TabTabGo.Core.Api.Controllers;

/// <summary>The in-memory half of the scope check on a patch: a second layer in front of the database check.</summary>
internal static class ScopeCheck
{
    // compiled once per scope instance; weak, so a scope built per request does not pin its delegate
    private static readonly ConditionalWeakTable<LambdaExpression, Delegate?> Compiled = new();

    /// <summary>
    /// True only when the scope definitely evaluates to false for the loaded entity. Anything that cannot be decided in memory is
    /// "unknown" and returns false: the database check after the save decides. That is every scope that reads a navigation or a
    /// collection (not loaded means null, empty or false in memory, never the database's answer) and any evaluation that throws.
    /// </summary>
    /// <param name="scope">The scope; null means none</param>
    /// <param name="entity">The loaded and patched entity</param>
    internal static bool IsDefinitelyOutside<TEntity>(Expression<Func<TEntity, bool>>? scope, TEntity entity)
    {
        if (scope == null || TouchesNavigation(scope))
        {
            return false;
        }

        try
        {
            var compiled = (Func<TEntity, bool>)Compiled.GetValue(scope, s => ((Expression<Func<TEntity, bool>>)s).Compile())!;
            return !compiled(entity);
        }
        catch (Exception)
        {
            return false;
        }
    }

    // a member of the entity whose type is not a scalar: a navigation, or a collection of them
    private static bool TouchesNavigation(LambdaExpression scope)
    {
        var finder = new NavigationFinder(scope.Parameters[0]);
        finder.Visit(scope.Body);
        return finder.Found;
    }

    private sealed class NavigationFinder(ParameterExpression parameter) : ExpressionVisitor
    {
        public bool Found { get; private set; }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (Unwrap(node.Expression) == parameter && IsReference(Nullable.GetUnderlyingType(node.Type) ?? node.Type))
            {
                Found = true;
            }

            return base.VisitMember(node);
        }

        private static bool IsReference(Type type) =>
            !type.IsValueType && type != typeof(string) && type != typeof(byte[]) && !type.IsPrimitive;

        private static Expression? Unwrap(Expression? expression)
        {
            while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.TypeAs } unary)
            {
                expression = unary.Operand;
            }

            return expression;
        }
    }
}
