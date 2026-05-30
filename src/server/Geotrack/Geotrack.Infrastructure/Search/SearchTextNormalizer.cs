using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Geotrack.Infrastructure.Search;

public static partial class SearchTextNormalizer
{
    public static string Normalize(params string?[] values)
    {
        var value = string.Join(' ', values.Where(text => !string.IsNullOrWhiteSpace(text)));
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var lower = value.Trim().ToLowerInvariant()
            .Replace("đ", "dj", StringComparison.Ordinal)
            .Replace("ð", "dj", StringComparison.Ordinal);
        var decomposed = lower.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }

        return WhitespacePattern()
            .Replace(builder.ToString().Normalize(NormalizationForm.FormC), " ")
            .Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
