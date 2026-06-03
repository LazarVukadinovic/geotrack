using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Geotrack.Infrastructure.Services.Agent;

internal static class AgentTextNormalizer
{
    private static readonly Regex SeparatorPattern = new(@"[^a-z0-9]+", RegexOptions.Compiled);
    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.Compiled);

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        var separated = SeparatorPattern.Replace(builder.ToString().Normalize(NormalizationForm.FormC), " ");
        return WhitespacePattern.Replace(separated, " ").Trim();
    }

    public static List<string> Tokenize(string? value) =>
        Normalize(value)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(TokenVariants)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    public static bool ContainsPhrase(string normalizedValue, string normalizedPhrase)
    {
        if (string.IsNullOrWhiteSpace(normalizedValue) || string.IsNullOrWhiteSpace(normalizedPhrase))
        {
            return false;
        }

        return normalizedValue == normalizedPhrase ||
            normalizedValue.StartsWith(normalizedPhrase + " ", StringComparison.Ordinal) ||
            normalizedValue.EndsWith(" " + normalizedPhrase, StringComparison.Ordinal) ||
            normalizedValue.Contains(" " + normalizedPhrase + " ", StringComparison.Ordinal);
    }

    public static bool TokenMatchesValue(string token, string normalizedValue) =>
        normalizedValue == token ||
        normalizedValue.StartsWith(token + " ", StringComparison.Ordinal) ||
        normalizedValue.EndsWith(" " + token, StringComparison.Ordinal) ||
        normalizedValue.Contains(" " + token + " ", StringComparison.Ordinal) ||
        normalizedValue.Contains(token, StringComparison.Ordinal);

    public static IEnumerable<string> TermVariants(string? value)
    {
        var normalized = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            yield break;
        }

        yield return normalized;
        yield return normalized.Replace(" ", "-");
        yield return normalized.Replace("-", " ");

        foreach (var token in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var variant in TokenVariants(token))
            {
                yield return variant;
            }
        }
    }

    public static IEnumerable<string> TokenVariants(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            yield break;
        }

        yield return token;

        if (token.EndsWith("vcu", StringComparison.Ordinal) && token.Length > 5)
        {
            yield return token[..^3] + "vac";
        }

        if (token.EndsWith("u", StringComparison.Ordinal) && token.Length > 4)
        {
            var stem = token[..^1];
            yield return stem;
            yield return stem + "s";
            yield break;
        }

        if (token.EndsWith("ies", StringComparison.Ordinal) && token.Length > 3)
        {
            yield return token[..^3] + "y";
        }
        else if (token.EndsWith("s", StringComparison.Ordinal) && token.Length > 2)
        {
            yield return token[..^1];
        }
        else if (token.Length > 2)
        {
            yield return token + "s";
        }

        if (token.EndsWith("vc", StringComparison.Ordinal) && token.Length > 4)
        {
            yield return token[..^1] + "ac";
        }

        if (token.EndsWith("vcs", StringComparison.Ordinal) && token.Length > 5)
        {
            yield return token[..^2] + "ac";
        }

        if (token.EndsWith("iji", StringComparison.Ordinal) && token.Length > 5)
        {
            yield return token[..^3] + "ija";
        }
    }
}
