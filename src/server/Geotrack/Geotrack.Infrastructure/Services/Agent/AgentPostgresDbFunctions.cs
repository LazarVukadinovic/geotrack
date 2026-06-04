using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services.Agent;

public static class AgentPostgresDbFunctions
{
    [DbFunction("unaccent", IsBuiltIn = true)]
    public static string Unaccent(string value) =>
        throw new InvalidOperationException("This method is translated to PostgreSQL unaccent().");
}
