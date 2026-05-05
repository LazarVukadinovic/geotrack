using System.Text.RegularExpressions;

namespace Geotrack.Application.Services;

public static class DisplayTextSafety
{
    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex OpaqueTokenPattern = new(
        @"(?i)(?:^|[\s_-])(?:[0-9a-f]{12,}|[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12})(?=$|[\s_-])",
        RegexOptions.Compiled);
    private static readonly Regex SlugSeparatorPattern = new(@"[-_]+", RegexOptions.Compiled);

    public static string CleanRequired(string? value, string fallback)
    {
        var cleaned = CleanOptional(value);
        return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
    }

    public static string? CleanOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = OpaqueTokenPattern.Replace(value.Trim(), " ");
        cleaned = WhitespacePattern.Replace(cleaned, " ").Trim(' ', '-', '_', '.', '#', ':');

        return IsOpaque(cleaned) ? null : cleaned;
    }

    public static string CleanLabelOrHumanizedKey(string? label, string? key, string fallback)
    {
        var cleaned = CleanOptional(label);
        if (!string.IsNullOrWhiteSpace(cleaned))
        {
            return cleaned;
        }

        var humanized = HumanizeKey(key);
        return string.IsNullOrWhiteSpace(humanized) ? fallback : humanized;
    }

    public static List<string> CleanLabels(IEnumerable<string?> values) =>
        values
            .Select(CleanOptional)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value)
            .ToList();

    private static string? HumanizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = CleanOptional(SlugSeparatorPattern.Replace(value.Trim(), " "));
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return null;
        }

        return string.Join(
            ' ',
            cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(word => word.Length == 1
                    ? word.ToUpperInvariant()
                    : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));
    }

    private static bool IsOpaque(string value)
    {
        if (value.Length == 0)
        {
            return true;
        }

        var compact = value.Replace("-", string.Empty).Replace("_", string.Empty);
        return compact.Length >= 8 && compact.All(Uri.IsHexDigit);
    }
}
