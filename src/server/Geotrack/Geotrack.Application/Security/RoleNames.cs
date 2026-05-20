namespace Geotrack.Application.Security;

public static class RoleNames
{
    public const string Admin = "Admin";
    public const string RegionalManager = "RegionalManager";
    public const string TerritoryManager = "TerritoryManager";
    public const string TerritoryAssociate = "TerritoryAssociate";
    public const string RegisteredUser = "RegisteredUser";

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
            "regionalmanager" => RegionalManager,
            "nationalmanager" => TerritoryManager,
            "territorymanager" => TerritoryManager,
            "territoryassociate" => TerritoryAssociate,
            "registereduser" => RegisteredUser,
            _ => roleName.Trim()
        };
    }
}
