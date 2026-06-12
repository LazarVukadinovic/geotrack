namespace Geotrack.Application.Options;

public class EmailOptions
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = "GeoTrack";
    public string FrontendBaseUrl { get; set; } = "http://localhost:4200";
    public string AppName { get; set; } = "GeoTrack";
    public int EmailVerificationTokenLifetimeHours { get; set; } = 24;
    public int PasswordResetTokenLifetimeMinutes { get; set; } = 30;
}
