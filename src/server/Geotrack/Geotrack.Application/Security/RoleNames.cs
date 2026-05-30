namespace Geotrack.Application.Security;

public static class RoleNames
{
    public const string Admin = "Admin";
    public const string ContentModerator = "ContentModerator";
    public const string RegionalManager = "RegionalManager";
    public const string TerritoryManager = "TerritoryManager";
    public const string TerritoryAssociate = "TerritoryAssociate";
    public const string RegisteredUser = "RegisteredUser";

    public static readonly IReadOnlyCollection<string> AdminAppRoles =
    [
        Admin,
        ContentModerator,
        TerritoryManager,
        TerritoryAssociate,
        RegionalManager
    ];

    public static bool CanAccessAdminApp(IEnumerable<string?> roles)
    {
        var normalizedRoles = roles
            .Select(Normalize)
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .ToHashSet(StringComparer.Ordinal);

        return AdminAppRoles.Any(role => normalizedRoles.Contains(role));
    }

    public static string Normalize(string? roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName))
            return string.Empty;

        var compact = new string(roleName
            .Where(ch => !char.IsWhiteSpace(ch) && ch != '-' && ch != '_')
            .ToArray());

        return compact.ToLowerInvariant() switch
        {
            "admin" => Admin,
            "administrator" => Admin,
            "superadmin" => Admin,
            "sysadmin" => Admin,
            "contentmoderator" => ContentModerator,
            "moderator" => ContentModerator,
            "regionalmanager" => RegionalManager,
            "nationalmanager" => TerritoryManager,
            "territorymanager" => TerritoryManager,
            "territoryassociate" => TerritoryAssociate,
            "registereduser" => RegisteredUser,
            _ => roleName.Trim()
        };
    }
}
