using System.Text;
using Microsoft.Extensions.Logging;


namespace Nma.Lms.Test.Helpers;

public static class Tools
{
    public static string GetUntilOrEmpty(this string text, string stopAt = "-")
    {
        if (!String.IsNullOrWhiteSpace(text))
        {
            int charLocation = text.IndexOf(stopAt, StringComparison.Ordinal);

            if (charLocation > 0)
                return text.Substring(0, charLocation);

        }
        return String.Empty;
    }
}
