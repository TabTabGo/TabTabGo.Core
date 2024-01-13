// ReSharper disable All
namespace TabTabGo.Core.Comparer;


public class JsonNodeComparer : IEqualityComparer<JsonNode>, IDisposable
{
    public string[] IgnoredProperties { get; set; }
    public ICollection<(string propName, JsonNode actualValue, JsonNode expectedValue)> DifferentProperties { get; set; } = new List<(string propName, JsonNode actualValue, JsonNode expectedValue)>();

    public JsonNodeComparer(params string[] ignoredProps)
    {
        IgnoredProperties = ignoredProps;
    }

    public IEnumerable<string>? GetDifferentProperties()
    {
        return DifferentProperties?.Select(prop => prop.propName).ToArray();
    }
    public void Dispose()
    {
        //DifferentProperties?.c();
        DifferentProperties = null;
    }

    /// <summary>
    /// Actual object should include all properties and values in expected object but it is ok for Actual object to have more properties 
    /// </summary>
    /// <param name="actual"></param>
    /// <param name="expected"></param>
    /// <returns></returns>
    public bool Equals(JsonNode? x, JsonNode? y)
    {
         // If both are null, or both are same instance, return true.
        if (ReferenceEquals(x, y))
        {
            return true;
        }

        // If one is null, but not both, return false.
        if (x is null || y is null)
        {
            return false;
        }

        // Compare the JSON strings of the two JsonNodes
        return x.ToString() == y.ToString();

    }

    public int GetHashCode(JsonNode obj)
    {
        // If obj is null, return 0
        if (obj is null)
        {
            return 0;
        }

        // Otherwise, return the hash code of the JSON string of the JsonNode
        return obj.ToString().GetHashCode();
    }
}

