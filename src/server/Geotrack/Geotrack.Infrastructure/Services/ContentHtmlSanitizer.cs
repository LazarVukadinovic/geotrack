using Ganss.Xss;
using Geotrack.Application.Interfaces.Service;

namespace Geotrack.Infrastructure.Services;

public sealed class ContentHtmlSanitizer : IContentHtmlSanitizer
{
    private static readonly string[] RichTextTags =
    {
        "p",
        "br",
        "strong",
        "em",
        "u",
        "ul",
        "ol",
        "li",
        "a",
        "h2",
        "h3",
        "blockquote"
    };

    private static readonly string[] RichTextAttributes =
    {
        "href",
        "rel",
        "title"
    };

    public string SanitizeRichText(string? html)
    {
        var trimmed = html?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return string.Empty;

        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedCssProperties.Clear();
        sanitizer.AllowedSchemes.Clear();
        sanitizer.UriAttributes.Clear();

        foreach (var tag in RichTextTags)
            sanitizer.AllowedTags.Add(tag);

        foreach (var attribute in RichTextAttributes)
            sanitizer.AllowedAttributes.Add(attribute);

        sanitizer.AllowedSchemes.Add("http");
        sanitizer.AllowedSchemes.Add("https");
        sanitizer.AllowedSchemes.Add("mailto");
        sanitizer.UriAttributes.Add("href");

        return sanitizer.Sanitize(trimmed).Trim();
    }
}
