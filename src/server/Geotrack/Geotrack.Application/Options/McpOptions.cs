namespace Geotrack.Application.Options;

public class McpOptions
{
    public bool Enabled { get; set; }
    public string PublicBaseUrl { get; set; } = string.Empty;
    public bool RequireAuthForPrivateTools { get; set; } = true;
    public int MaxResults { get; set; } = 10;
}
