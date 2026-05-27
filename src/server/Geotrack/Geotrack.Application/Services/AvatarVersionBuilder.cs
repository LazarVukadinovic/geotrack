namespace Geotrack.Application.Services;

public static class AvatarVersionBuilder
{
    public static long? From(string? avatarUrl, DateTime? updatedAt, DateTime createdAt)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl))
            return null;

        var timestamp = updatedAt ?? createdAt;
        if (timestamp == default)
            return null;

        return ToUtcOffset(timestamp).ToUnixTimeMilliseconds();
    }

    public static long From(DateTime updatedAt) => ToUtcOffset(updatedAt).ToUnixTimeMilliseconds();

    private static DateTimeOffset ToUtcOffset(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

        return new DateTimeOffset(utc);
    }
}
