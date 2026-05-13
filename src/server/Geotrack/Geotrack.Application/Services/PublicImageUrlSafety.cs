namespace Geotrack.Application.Services;

public static class PublicImageUrlSafety
{
    private static readonly string[] PublicPathPrefixes =
    [
        "/media/",
        "/images/"
    ];

    public static string? Normalize(string? value)
    {
        var cleaned = value?.Trim();
        if (string.IsNullOrWhiteSpace(cleaned) ||
            cleaned.Length > 2048 ||
            cleaned.Any(char.IsWhiteSpace) ||
            cleaned.Any(char.IsControl) ||
            cleaned.Contains('\\', StringComparison.Ordinal))
        {
            return null;
        }

        if (TryNormalizePublicPath(cleaned, out var publicPath))
        {
            return publicPath;
        }

        if (!Uri.TryCreate(cleaned, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return cleaned;
        }

        if (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            uri.IsLoopback &&
            TryNormalizePublicPath(uri.PathAndQuery, out publicPath))
        {
            return publicPath;
        }

        return null;
    }

    private static bool TryNormalizePublicPath(string value, out string normalized)
    {
        normalized = string.Empty;
        if (value.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        var candidate = value;
        if (candidate.StartsWith("media/", StringComparison.Ordinal) ||
            candidate.StartsWith("images/", StringComparison.Ordinal))
        {
            candidate = "/" + candidate;
        }

        if (!PublicPathPrefixes.Any(prefix => candidate.StartsWith(prefix, StringComparison.Ordinal)))
        {
            return false;
        }

        if (ContainsUnsafePathSegment(candidate))
        {
            return false;
        }

        normalized = candidate;
        return true;
    }

    private static bool ContainsUnsafePathSegment(string value)
    {
        var path = value.Split('?', '#')[0];
        if (path.Contains("%2e", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("%2f", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("%5c", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return path
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or "..");
    }
}
