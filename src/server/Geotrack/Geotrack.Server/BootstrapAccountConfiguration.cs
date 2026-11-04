using System.Net.Mail;
using Geotrack.Application.Security;

namespace Geotrack.Server;

// Do not use a record here: generated ToString() must not expose the password.
public sealed class BootstrapAccountConfiguration
{
    public string Name { get; private init; } = string.Empty;
    public string Role { get; private init; } = string.Empty;
    public string Email { get; private init; } = string.Empty;
    public string Username { get; private init; } = string.Empty;
    public string Password { get; private init; } = string.Empty;
    public string FirstName { get; private init; } = string.Empty;
    public string LastName { get; private init; } = string.Empty;
    public string DisplayName { get; private init; } = string.Empty;

    private static readonly (string Name, string Role)[] Accounts =
    [
        ("Admin", RoleNames.Admin),
        ("ContentModerator", RoleNames.ContentModerator),
        ("CroatiaTerritoryManager", RoleNames.TerritoryManager),
        ("CroatiaRegionalManager", RoleNames.RegionalManager),
        ("SerbiaTerritoryManager", RoleNames.TerritoryManager),
        ("SerbiaRegionalManager", RoleNames.RegionalManager)
    ];

    public static IReadOnlyList<BootstrapAccountConfiguration> Read(IConfiguration configuration)
    {
        // The old alias is retained, but neither environment has an implicit opt-in.
        var enabled = configuration.GetValue<bool?>(OperationalUserSeeder.EnabledConfigKey)
            ?? configuration.GetValue<bool?>(OperationalUserSeeder.OperationalUsersEnabledConfigKey)
            ?? false;
        if (!enabled)
            return [];

        var result = new List<BootstrapAccountConfiguration>();
        foreach (var (name, role) in Accounts)
        {
            var section = configuration.GetSection($"Seed:OperationalUsers:{name}");
            if (!section.GetValue<bool>("Enabled"))
                continue;

            var email = section["Email"]?.Trim().ToLowerInvariant();
            var username = section["Username"]?.Trim();
            var password = section["Password"];
            var firstName = section["FirstName"]?.Trim() ?? name;
            var lastName = section["LastName"]?.Trim() ?? "";
            var displayName = section["DisplayName"]?.Trim() ?? username;
            if (string.IsNullOrWhiteSpace(email) || email.Length > 200 ||
                !MailAddress.TryCreate(email, out var address) || address.Address != email ||
                !string.IsNullOrEmpty(address.DisplayName) ||
                string.IsNullOrWhiteSpace(username) || username.Length > 100 ||
                username.Any(char.IsControl))
                throw new InvalidOperationException($"Seed:OperationalUsers:{name} requires a valid Email and Username.");
            if (firstName.Length > 100 || lastName.Length > 100 || displayName?.Length > 100)
                throw new InvalidOperationException($"Seed:OperationalUsers:{name} profile names cannot exceed 100 characters.");

            // Fail before opening a database transaction. Never trim or print credentials.
            if (string.IsNullOrWhiteSpace(password) || password.Length < 16 || password.Length > 256 ||
                password.Contains('<') || password.Contains('>') ||
                password.Distinct().Count() < 10 || password.Any(char.IsControl) ||
                !password.Any(char.IsUpper) || !password.Any(char.IsLower) ||
                !password.Any(char.IsDigit) || !password.Any(ch => !char.IsLetterOrDigit(ch)) ||
                password.Contains(username, StringComparison.OrdinalIgnoreCase) ||
                password.Contains(email, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Seed:OperationalUsers:{name}:Password must be supplied securely and satisfy the bootstrap password policy.");

            result.Add(new BootstrapAccountConfiguration
            {
                Name = name, Role = role, Email = email, Username = username!, Password = password,
                FirstName = firstName, LastName = lastName, DisplayName = displayName!
            });
        }

        if (result.Count == 0)
            throw new InvalidOperationException("Operational bootstrap requires at least one explicitly enabled account.");
        if (result.Select(a => a.Email).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Count ||
            result.Select(a => a.Username).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Count)
            throw new InvalidOperationException("Bootstrap accounts must have distinct emails and usernames.");

        return result;
    }
}
