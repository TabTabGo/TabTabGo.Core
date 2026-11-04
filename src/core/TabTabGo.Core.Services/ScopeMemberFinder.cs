using System.Linq.Expressions;
using System.Reflection;

namespace TabTabGo.Core.Services;

/// <summary>Finds the members of the entity a scope predicate refers to.</summary>
internal static class ScopeMemberFinder
{
    /// <summary>
    /// The names of the entity members the scope reads (<c>p.Tenant</c>, <c>p.Category.Name</c> gives <c>Category</c>, and its
    /// <c>CategoryId</c> foreign key when the entity has one, <c>EF.Property(p, "Name")</c> gives <c>Name</c>), case-insensitive.
    /// </summary>
    /// <param name="scope">The scope; null gives an empty set</param>
    /// <param name="entityType">Entity type, to look the foreign keys up</param>
    internal static IReadOnlySet<string> Find(LambdaExpression? scope, Type entityType)
    {
        var members = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (scope == null)
        {
            return members;
        }

        var visitor = new Visitor(scope.Parameters[0], members);
        visitor.Visit(scope.Body);
        foreach (var name in members.ToArray())
        {
            var property = entityType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property != null && IsNavigation(property.PropertyType))
            {
                // by convention <Navigation>Id; the model is not known here
                if (entityType.GetProperty(property.Name + "Id", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) is { } fk)
                {
                    members.Add(fk.Name);
                }
            }
        }

        return members;
    }

    private static bool IsNavigation(Type type) =>
        type != typeof(string) && !type.IsValueType && !type.IsArray;

    private sealed class Visitor(ParameterExpression parameter, HashSet<string> members) : ExpressionVisitor
    {
        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression == parameter)
            {
                members.Add(node.Member.Name);
            }

            return base.VisitMember(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            // EF.Property(p, "Name")
            if (node.Method.Name == "Property" && node.Arguments.Count == 2 && node.Arguments[0] == parameter
                && node.Arguments[1] is ConstantExpression { Value: string name })
            {
                members.Add(name);
            }

            return base.VisitMethodCall(node);
        }
    }
}
