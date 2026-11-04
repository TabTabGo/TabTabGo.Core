namespace TabTabGo.Core.Comparer;

/// <summary>
/// Compares two JSON arrays ignoring item order. Items are compared with <see cref="JsonNodeComparer"/>;
/// nested arrays are compared recursively.
/// </summary>
public class JsonArrayComparer : IEqualityComparer<JsonArray>, IDisposable
{
    /// <summary>Property names ignored while comparing objects.</summary>
    public string[] IgnoredProperties { get; set; }

    /// <summary>Creates a comparer.</summary>
    /// <param name="ignoredProps">Property names to ignore</param>
    public JsonArrayComparer(params string[] ignoredProps)
    {
        IgnoredProperties = ignoredProps;
    }

    /// <inheritdoc />
    public bool Equals(JsonArray? x, JsonArray? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;
        if (x.Count != y.Count) return false;
        using var jCompare = new JsonNodeComparer(IgnoredProperties);

        var comparedItems = new HashSet<int>();
        foreach (var xItem in x)
        {
            var found = false;
            for (var i = 0; i < y.Count; i++)
            {
                if (comparedItems.Contains(i)) continue;

                var yItem = y[i];
                var same = xItem is JsonArray xArray && yItem is JsonArray yArray
                    ? Equals(xArray, yArray)
                    : jCompare.Equals(xItem, yItem);
                if (same)
                {
                    comparedItems.Add(i);
                    found = true;
                    break;
                }
            }

            if (!found) return false;
        }

        return true;
    }

    /// <inheritdoc />
    public int GetHashCode(JsonArray obj)
    {
        using var jCompare = new JsonNodeComparer(IgnoredProperties);
        return obj.Aggregate(0, (hash, item) => unchecked(hash + (item is JsonArray array ? GetHashCode(array) : jCompare.GetHashCode(item))));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
