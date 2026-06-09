namespace Geotrack.Application.Interfaces.Service;

public interface IContentHtmlSanitizer
{
    string SanitizeRichText(string? html);
}
