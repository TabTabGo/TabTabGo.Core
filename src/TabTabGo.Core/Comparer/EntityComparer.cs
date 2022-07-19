

namespace TabTabGo.Core.Comparer
{
    public class EntityComparer : IEqualityComparer<IEntity>, IDisposable
    {
        public List<PropertyInfo> DifferentProperties { get; set; } = new List<PropertyInfo>();

        public IEnumerable<string> GetDifferentProeprties()
        {
            return DifferentProperties?.Select(prop => prop.Name).ToArray();
        }
        public void Dispose()
        {
            DifferentProperties?.Clear();
            DifferentProperties = null;
        }

        public bool Equals(IEntity x, IEntity y)
        {
            if (x == null && y == null) return true;
            if (x == null && y != null) return false;
            if (x != null && y == null) return false;
            var isEqual = true;
            var properties = x.GetType().GetProperties(System.Reflection.BindingFlags.Public);
            foreach (var prop in properties)
            {
                if (prop.Name == "CreatedBy" || prop.Name == "CreatedDate" || prop.Name == "UpdatedBy" || prop.Name == "UpdatedDate") continue;

                var xValue = prop.GetValue(x);
                var yValue = prop.GetValue(y);
                if (xValue == null && yValue == null) continue;
                if (xValue != null && yValue == null)
                {
                    DifferentProperties.Add(prop);
                    isEqual = false;
                    continue;
                }
                if (xValue == null && yValue != null)
                {
                    DifferentProperties.Add(prop);
                    isEqual = false;
                    continue;
                }
                if (prop.PropertyType.IsSubclassOf(typeof(Entity)))
                {
                    using (var entityComparer = new EntityComparer())
                    {
                        if (!entityComparer.Equals(xValue as Entity, yValue as Entity))
                        {
                            DifferentProperties.Add(prop);
                            isEqual = false;
                        }
                    }

                }
                else if (prop.PropertyType.IsArray || typeof(IEnumerable).IsAssignableFrom(prop.PropertyType) || typeof(ICollection).IsAssignableFrom(prop.PropertyType))
                {
                    //TODO compare Collections;

                }
                else if (!xValue.Equals(yValue))
                {
                    DifferentProperties.Add(prop);
                    isEqual = false;
                    continue;
                }

            }

            return isEqual;

        }

        public int GetHashCode(IEntity obj)
        {
            return obj.GetHashCode();
        }


    }
}
