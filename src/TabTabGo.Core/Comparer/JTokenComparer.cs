namespace TabTabGo.Core.Comparer;


public class JTokenComparer : IEqualityComparer<JToken>, IDisposable
{

    public string[] IgnoredProperties { get; set; }
    public ICollection<(string propName, JToken actualValue, JToken expectedValye)> DifferentProperties { get; set; } = new List<(string propName, JToken actualValue, JToken expectedValye)>();

    public JTokenComparer(params string[] ignoredProps)
    {
        IgnoredProperties = ignoredProps;
    }

    public IEnumerable<string> GetDifferentProeprties()
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
    public bool Equals(JToken actual, JToken expected)
    {
        if (actual == null && expected == null) return true;
        if (actual == null && expected != null) return false;
        if (actual != null && expected == null) return false;
        var isEqual = true;
        var actualChildern = actual.Children().OrderBy(c => c.Path);
        var expectedChildren = expected.Children().OrderBy(c => c.Path);
        var jArrayCompare = new JArrayComparer(IgnoredProperties);
        foreach (var eChild in expectedChildren)
        {
            var aChild = actualChildern.FirstOrDefault(a => a.Path.Equals(eChild.Path, StringComparison.CurrentCultureIgnoreCase));

            if (aChild == null)
            {
                if (eChild is JProperty && IgnoredProperties != null)
                {
                    if (IgnoredProperties.Contains(((JProperty)eChild).Name)) continue;
                }
                DifferentProperties.Add((eChild.Path, null, eChild));
                isEqual = false;
                continue;
            }
            if (aChild is JProperty && eChild is JProperty)
            {
                var paChild = aChild as JProperty;
                var peChild = eChild as JProperty;
                if (IgnoredProperties != null && IgnoredProperties.Contains(peChild.Name)) continue;

                if (paChild.Value is JValue && peChild.Value is JValue)
                {

                    var veChild = peChild.Value as JValue;
                    var vaChild = paChild.Value as JValue;

                    if (vaChild.Value is DateTime)
                    {
                        var veDate = DateTime.Parse(veChild.ToString());
                        var vaDate = DateTime.Parse(vaChild.ToString());
                        if (veDate != vaDate)
                        {
                            DifferentProperties.Add((eChild.Path, aChild, eChild));
                            isEqual = false;
                        }
                    }
                    else if (vaChild.Value is DateTimeOffset)
                    {
                        var veDate = DateTimeOffset.Parse(veChild.ToString());
                        var vaDate = DateTimeOffset.Parse(vaChild.ToString());
                        if (veDate != vaDate)
                        {
                            DifferentProperties.Add((eChild.Path, aChild, eChild));
                            isEqual = false;
                        }
                    }
                    else if (!veChild.ToString().Equals(vaChild.ToString(), StringComparison.CurrentCultureIgnoreCase))
                    {
                        DifferentProperties.Add((eChild.Path, aChild, eChild));
                        isEqual = false;
                    }

                    continue;
                }
                if (paChild.Value.Type == JTokenType.Array && peChild.Value.Type == JTokenType.Array && !jArrayCompare.Equals(paChild.Value as JArray, peChild.Value as JArray))
                {
                    DifferentProperties.Add((peChild.Path, paChild.Value, peChild.Value));
                    isEqual = false;
                    continue;
                }
                if (!Equals(paChild.Value, peChild.Value))
                {
                    DifferentProperties.Add((peChild.Path, paChild.Value, peChild.Value));
                    isEqual = false;
                    continue;
                }
            }
            else if (aChild.Type == JTokenType.Array && eChild.Type == JTokenType.Array && !jArrayCompare.Equals(aChild as JArray, eChild as JArray))
            {
                DifferentProperties.Add((eChild.Path, aChild.First, eChild.First));
                isEqual = false;
                continue;
            }
            else if (!Equals(aChild, eChild))
            {
                //DifferentProperties.Add((eChild.Path, aChild, eChild));
                isEqual = false;
                continue;
            }
        }
        return isEqual;

    }

    public int GetHashCode(JToken obj)
    {
        return obj.GetHashCode();
    }
}

