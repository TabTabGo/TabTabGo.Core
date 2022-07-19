using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TabTabGo.Core.Entities;

namespace TabTabGo.Core.Extensions
{
    public static class EntityExtensions
    {
        public static void CopyObject(this object source, Type type, object destination,
            params string[] ignoredProperties)
        {
            var publicProp = type;
            var pubProps = publicProp.GetProperties();
            foreach (var prop in pubProps)
            {
                var atts = prop.GetCustomAttributes<IgnoreCopyAttribute>();
                if (atts.Any(a => a.IgnoreCopy)) continue;
                if (ignoredProperties != null && ignoredProperties.Contains(prop.Name) &&
                    prop.GetSetMethod() == null) continue;

                if (prop.PropertyType.IsArray
                    || (prop.PropertyType.IsGenericType &&
                        prop.PropertyType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    || (prop.PropertyType.IsGenericType &&
                        prop.PropertyType.GetGenericTypeDefinition() == typeof(ICollection<>))
                )
                {
                    continue;
                }

                if (prop.PropertyType != typeof(string) && prop.PropertyType.IsClass &&
                    prop.PropertyType.GetInterface("IEntity", true) == typeof(IEntity))
                {
                    var propAtts = prop.PropertyType.GetCustomAttributes<IgnoreCopyAttribute>();
                    if (propAtts.Any(a => a.IgnoreCopy)) continue;

                    var spObject = prop.GetValue(source);
                    if (spObject == null) continue;

                    var dpObject = prop.GetValue(destination);
                    if (dpObject != null)
                    {
                        spObject.CopyObject(prop.PropertyType, dpObject, ignoredProperties);
                        continue;
                    }
                }

                var spValue = prop.GetValue(source);
                var dpValue = prop.GetValue(destination);
                if ((prop.PropertyType.IsPrimitive
                     || prop.PropertyType == typeof(string)
                     || prop.PropertyType == typeof(DateTime)
                     || prop.PropertyType == typeof(DateTimeOffset))
                    && spValue != dpValue)
                {
                    prop.SetValue(destination, spValue);
                    continue;
                }

                prop.SetValue(destination, spValue);
            }
        }

        public static void CopyObject<T>(this T source, T destination, params string[] ignoredProperties)
            where T : class
        {
            destination ??= Clone<T>(source);
            source.CopyObject(typeof(T), destination, ignoredProperties);
        }

        public static T Clone<T>(this T source) where T : class
        {
            T newObj = (T) Activator.CreateInstance(typeof(T));
            return source;
        }
    }

    [System.AttributeUsage(System.AttributeTargets.Property | AttributeTargets.Class, Inherited = true,
        AllowMultiple = true)]
    public sealed class IgnoreCopyAttribute : Attribute
    {
        public bool IgnoreCopy { get; set; }

        // This is a positional argument
        public IgnoreCopyAttribute(bool ignore = true)
        {
            IgnoreCopy = ignore;
        }
    }
}