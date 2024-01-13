
namespace TabTabGo.Core.Comparer;

public class JsonArrayComparer : IEqualityComparer<JsonArray>, IDisposable
{
    public string[] IgnoredProperties { get; set; }

    public JsonArrayComparer(params string[] ignoredProps)
    {
        IgnoredProperties = ignoredProps;
    }
    public bool Equals(JsonArray? x, JsonArray? y)
    {
        if (x == null && y == null) return true;
        if (x != null && y == null) return false;
        if (x == null && y != null) return false;
        if (x.Count() != y.Count()) return false;
        var jCompare = new JsonNodeComparer(IgnoredProperties);

        var comparedItems = new List<int>();
        foreach (var xItem in x)
        {
            var compareResult = false;
            for (int i = 0; i < y.Count; i++)
            {
                if (comparedItems.Contains(i)) continue;

                var yItem = y[i];
                if (xItem is JsonArray && yItem is JsonArray)
                {
                    if (Equals(xItem, yItem))
                    {
                        comparedItems.Add(i);
                        compareResult = true;
                        break;
                    }
                    else continue;
                }
                if (xItem is JsonNode && yItem is JsonNode)
                {
                    if (jCompare.Equals(xItem, yItem))
                    {
                        comparedItems.Add(i);
                        compareResult = true;
                        break;
                    }
                    else
                    {
                        continue;
                    }
                }
            }
            if (!compareResult) return false;
        }
        return true;
    }

    public int GetHashCode(JsonArray obj)
    {
        return obj.GetHashCode();
    }

    public void Dispose()
    {

    }
}

