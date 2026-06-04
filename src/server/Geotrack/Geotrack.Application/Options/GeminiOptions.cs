namespace Geotrack.Application.Options;

public class GeminiOptions
{
    public const string DefaultModel = "gemini-3.5-flash";
    public const string DefaultBaseUrl = "https://generativelanguage.googleapis.com";

    public List<string> ApiKeys { get; set; } = new();
    public string Model { get; set; } = DefaultModel;
    public List<string> FallbackModels { get; set; } = new();
    public string BaseUrl { get; set; } = DefaultBaseUrl;
    public int KeyCooldownSeconds { get; set; } = 300;
    public int MaxAttemptsPerRequest { get; set; }
}
