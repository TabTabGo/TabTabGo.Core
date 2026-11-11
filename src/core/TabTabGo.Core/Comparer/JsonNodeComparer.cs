namespace TabTabGo.Core.Comparer;

/// <summary>
/// Compares two JSON nodes by value: property order and number formatting do not matter and
/// <see cref="IgnoredProperties"/> are removed (at any depth) before comparing.
/// </summary>
public class JsonNodeComparer : IEqualityComparer<JsonNode?>, IDisposable
{
    /// <summary>Property names ignored while comparing.</summary>
    public string[] IgnoredProperties { get; set; }

    /// <summary>Differences found by the last <see cref="Equals(JsonNode?, JsonNode?)"/> call.</summary>
    public ICollection<(string propName, JsonNode? actualValue, JsonNode? expectedValue)> DifferentProperties { get; set; } = new List<(string propName, JsonNode? actualValue, JsonNode? expectedValue)>();

    /// <summary>Creates a comparer.</summary>
    /// <param name="ignoredProps">Property names to ignore</param>
    public JsonNodeComparer(params string[] ignoredProps)
    {
        IgnoredProperties = ignoredProps;
    }

    /// <summary>Names of the properties that differed in the last comparison.</summary>
    public IEnumerable<string> GetDifferentProperties()
    {
        return DifferentProperties.Select(prop => prop.propName).ToArray();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        DifferentProperties.Clear();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Compares two nodes. The properties that differ (top level of two objects) are recorded in <see cref="DifferentProperties"/>.
    /// </summary>
    public bool Equals(JsonNode? x, JsonNode? y)
    {
        DifferentProperties.Clear();
        if (ReferenceEquals(x, y))
        {
            return true;
        }

        if (x is null || y is null)
        {
            return false;
        }

        var left = Strip(x);
        var right = Strip(y);
        if (JsonNode.DeepEquals(left, right))
        {
            return true;
        }

        if (left is JsonObject leftObject && right is JsonObject rightObject)
        {
            foreach (var name in leftObject.Select(p => p.Key).Union(rightObject.Select(p => p.Key)))
            {
                leftObject.TryGetPropertyValue(name, out var leftValue);
                rightObject.TryGetPropertyValue(name, out var rightValue);
                if (!JsonNode.DeepEquals(leftValue, rightValue))
                {
                    DifferentProperties.Add((name, leftValue?.DeepClone(), rightValue?.DeepClone()));
                }
            }
        }

        return false;
    }

    /// <inheritdoc />
    public int GetHashCode(JsonNode? obj)
    {
        return obj is null ? 0 : HashOf(Strip(obj));
    }

    private JsonNode Strip(JsonNode node)
    {
        var clone = node.DeepClone();
        if (IgnoredProperties is { Length: > 0 })
        {
            Remove(clone);
        }

        return clone;
    }

    private void Remove(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var ignored in IgnoredProperties)
                {
                    obj.Remove(ignored);
                }

                foreach (var (_, child) in obj.ToList())
                {
                    Remove(child);
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    Remove(item);
                }

                break;
        }
    }

    private static int HashOf(JsonNode? node)
    {
        unchecked
        {
            switch (node)
            {
                case null:
                    return 0;
                case JsonObject obj:
                    return obj.Aggregate(17, (hash, p) => hash ^ HashCode.Combine(p.Key, HashOf(p.Value)));
                case JsonArray array:
                    return array.Aggregate(19, (hash, item) => hash + HashOf(item));
                default:
                    var element = JsonSerializer.SerializeToElement(node);
                    return element.ValueKind switch
                    {
                        JsonValueKind.Number => element.GetDouble().GetHashCode(),
                        JsonValueKind.String => element.GetString()!.GetHashCode(), // justified: the node was checked to be a JSON string
                        var kind => kind.GetHashCode(),
                    };
            }
        }
    }
}
