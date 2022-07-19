namespace TabTabGo.Core.Comparer;

public class JArrayComparer : IEqualityComparer<JArray>, IDisposable
{
    public string[] IgnoredProperties { get; set; }

    public JArrayComparer(params string[] ignoredProps)
    {
        IgnoredProperties = ignoredProps;
    }
    public bool Equals(JArray x, JArray y)
    {
        if (x == null && y == null) return true;
        if (x != null && y == null) return false;
        if (x == null && y != null) return false;
        if (x.Count() != y.Count()) return false;
        var jCompare = new JTokenComparer(IgnoredProperties);

        var comparedItems = new List<int>();
        foreach (var xItem in x)
        {
            var xFistProp = xItem.First() as JProperty;
            var compareResult = false;
            for (int i = 0; i < y.Count; i++)
            {
                if (comparedItems.Contains(i)) continue;

                var yItem = y[i];
                if (xItem is JArray && yItem is JArray)
                {
                    if (Equals(xItem, yItem))
                    {
                        comparedItems.Add(i);
                        compareResult = true;
                        break;
                    }
                    else continue;
                }
                if (xItem is JToken && yItem is JToken)
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

    public int GetHashCode(JArray obj)
    {
        return obj.GetHashCode();
    }

    public void Dispose()
    {

    }
}

