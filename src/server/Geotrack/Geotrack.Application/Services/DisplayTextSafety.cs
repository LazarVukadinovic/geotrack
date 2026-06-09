using System.Net;
using System.Text.RegularExpressions;

namespace Geotrack.Application.Services;

public static class DisplayTextSafety
{
    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex OpaqueTokenPattern = new(
        @"(?i)(?:^|[\s_-])(?:[0-9a-f]{12,}|[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12})(?=$|[\s_-])",
        RegexOptions.Compiled);
    private static readonly Regex SlugSeparatorPattern = new(@"[-_]+", RegexOptions.Compiled);
    private static readonly Regex HtmlScriptStylePattern = new(
        @"(?is)<\s*(script|style)\b[^>]*>.*?<\s*/\s*\1\s*>",
        RegexOptions.Compiled);
    private static readonly Regex HtmlLineBreakPattern = new(@"(?i)<\s*br\s*/?\s*>", RegexOptions.Compiled);
    private static readonly Regex HtmlBlockStartPattern = new(
        @"(?i)<\s*(?:p|div|section|article|header|footer|aside|main|h[1-6]|ul|ol|li|blockquote|pre|table|thead|tbody|tfoot|tr)\b[^>]*>",
        RegexOptions.Compiled);
    private static readonly Regex HtmlBlockEndPattern = new(
        @"(?i)<\s*/\s*(?:p|div|section|article|header|footer|aside|main|h[1-6]|ul|ol|li|blockquote|pre|table|thead|tbody|tfoot|tr)\s*>",
        RegexOptions.Compiled);
    private static readonly Regex HtmlTagPattern = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex HtmlAllowedInlineStartPattern = new(
        @"(?i)<\s*(strong|b|em|i|u)\b[^>]*>",
        RegexOptions.Compiled);
    private static readonly Regex HtmlAllowedInlineEndPattern = new(@"(?i)<\s*/\s*(strong|b|em|i|u)\s*>", RegexOptions.Compiled);
    private static readonly Regex HtmlStyledSpanPattern = new(
        @"(?is)<\s*span\b[^>]*\bstyle\s*=\s*[""'](?<style>[^""']*)[""'][^>]*>(?<content>.*?)<\s*/\s*span\s*>",
        RegexOptions.Compiled);

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

    public static string? CleanOptionalMultiline(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = WebUtility.HtmlDecode(value.Trim()).Replace("\r\n", "\n").Replace('\r', '\n');
        normalized = HtmlScriptStylePattern.Replace(normalized, " ");
        normalized = HtmlLineBreakPattern.Replace(normalized, "\n");
        normalized = HtmlBlockEndPattern.Replace(normalized, "\n");
        normalized = HtmlBlockStartPattern.Replace(normalized, string.Empty);
        normalized = HtmlTagPattern.Replace(normalized, string.Empty);

        var lines = normalized
            .Split('\n')
            .Select(line => WhitespacePattern.Replace(OpaqueTokenPattern.Replace(line, " "), " ")
                .Trim(' ', '-', '_', '.', '#', ':'))
            .ToList();

        var cleanedLines = new List<string>();
        var consecutiveEmpty = 0;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                consecutiveEmpty++;
                continue;
            }

            if (cleanedLines.Count > 0 && consecutiveEmpty > 0)
            {
                cleanedLines.Add(string.Empty);
            }

            consecutiveEmpty = 0;
            cleanedLines.Add(line);
        }

        var cleaned = string.Join('\n', cleanedLines);
        return string.IsNullOrWhiteSpace(cleaned) || IsOpaque(cleaned.Replace("\n", " "))
            ? null
            : cleaned;
    }

    public static string? CleanOptionalRichMultiline(string? value)
    {
        var cleaned = CleanOptionalMultilinePreservingAllowedInlineHtml(value);
        return string.IsNullOrWhiteSpace(cleaned) || IsOpaque(HtmlTagPattern.Replace(cleaned, " "))
            ? null
            : cleaned;
    }

    public static string? CleanOptionalRichMultiline(string? value, int maxLength, string tooLongMessage)
    {
        var cleaned = CleanOptionalRichMultiline(value);
        if (cleaned is not null && cleaned.Length > maxLength)
        {
            throw new ArgumentException(tooLongMessage);
        }

        return cleaned;
    }

    private static string? CleanOptionalMultilinePreservingAllowedInlineHtml(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = WebUtility.HtmlDecode(value.Trim()).Replace("\r\n", "\n").Replace('\r', '\n');
        normalized = HtmlScriptStylePattern.Replace(normalized, " ");
        normalized = HtmlLineBreakPattern.Replace(normalized, "\n");
        normalized = HtmlBlockEndPattern.Replace(normalized, "\n");
        normalized = HtmlBlockStartPattern.Replace(normalized, string.Empty);
        normalized = HtmlStyledSpanPattern.Replace(normalized, ApplyAllowedSpanStyles);
        normalized = HtmlAllowedInlineStartPattern.Replace(normalized, match => $"<{match.Groups[1].Value.ToLowerInvariant()}>");
        normalized = HtmlAllowedInlineEndPattern.Replace(normalized, match => $"</{match.Groups[1].Value.ToLowerInvariant()}>");
        normalized = Regex.Replace(
            normalized,
            @"<(?!/?(?:strong|b|em|i|u)\b)[^>]+>",
            string.Empty,
            RegexOptions.IgnoreCase);

        var lines = normalized
            .Split('\n')
            .Select(line => WhitespacePattern.Replace(OpaqueTokenPattern.Replace(line, " "), " ")
                .Trim(' ', '-', '_', '.', '#', ':'))
            .ToList();

        var cleanedLines = new List<string>();
        var consecutiveEmpty = 0;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(HtmlTagPattern.Replace(line, string.Empty)))
            {
                consecutiveEmpty++;
                continue;
            }

            if (cleanedLines.Count > 0 && consecutiveEmpty > 0)
            {
                cleanedLines.Add(string.Empty);
            }

            consecutiveEmpty = 0;
            cleanedLines.Add(line);
        }

        var cleaned = string.Join('\n', cleanedLines);
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    private static string ApplyAllowedSpanStyles(Match match)
    {
        var style = match.Groups["style"].Value.ToLowerInvariant();
        var content = match.Groups["content"].Value;

        if (Regex.IsMatch(style, @"font-weight\s*:\s*(?:bold|[6-9]00)", RegexOptions.IgnoreCase))
        {
            content = $"<strong>{content}</strong>";
        }

        if (Regex.IsMatch(style, @"font-style\s*:\s*italic", RegexOptions.IgnoreCase))
        {
            content = $"<em>{content}</em>";
        }

        if (Regex.IsMatch(style, @"text-decoration(?:-line)?\s*:[^;]*underline", RegexOptions.IgnoreCase))
        {
            content = $"<u>{content}</u>";
        }

        return content;
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
