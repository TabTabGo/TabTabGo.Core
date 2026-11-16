namespace Nma.Lms.Test.Helpers;

/// <summary>
/// Legacy string helper. Removed in 11.x.
/// </summary>
[Obsolete("Removed in 11.x.")]
public static class Tools
{
    /// <summary>Returns the text before the first occurrence of <paramref name="stopAt"/>, or an empty string.</summary>
    /// <param name="text">Text</param>
    /// <param name="stopAt">Marker</param>
    public static string GetUntilOrEmpty(this string text, string stopAt = "-")
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            var charLocation = text.IndexOf(stopAt, StringComparison.Ordinal);

            if (charLocation > 0)
                return text.Substring(0, charLocation);
        }

        return string.Empty;
    }
}
