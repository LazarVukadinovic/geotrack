using System.Globalization;

namespace Geotrack.Infrastructure.Services;

internal static class CountryRegistry
{
    private const string KosovoIso2 = "XK";
    private const string KosovoGeoBoundariesIso3 = "XKX";

    private static readonly string[] KosovoIsoAliases = { "XKX", "XKK", "XK" };
    private static readonly string[] KosovoNames = { "KOSOVO", "KOSOVA", "REPUBLIC OF KOSOVO", "REPUBLIC OF KOSOVA" };
    private static readonly Lazy<IReadOnlyList<CountryRegistryItem>> CountriesLazy = new(BuildCountries);
    private static readonly Lazy<IReadOnlyDictionary<string, CountryRegistryItem>> ByIso2Lazy =
        new(() => CountriesLazy.Value.ToDictionary(item => item.Iso2, StringComparer.OrdinalIgnoreCase));
    private static readonly Lazy<IReadOnlyDictionary<string, CountryRegistryItem>> ByIso3Lazy =
        new(() => CountriesLazy.Value.ToDictionary(item => item.Iso3, StringComparer.OrdinalIgnoreCase));

    public static IReadOnlyList<CountryRegistryItem> Countries => CountriesLazy.Value;

    public static CountryRegistryItem? FindByIso2(string? iso2)
    {
        var normalized = NormalizeIso2(iso2);
        return normalized is not null && ByIso2Lazy.Value.TryGetValue(normalized, out var country)
            ? country
            : null;
    }

    public static CountryRegistryItem? FindByIso3(string? iso3)
    {
        var normalized = NormalizeIso3(iso3);
        return normalized is not null && ByIso3Lazy.Value.TryGetValue(normalized, out var country)
            ? country
            : null;
    }

    public static string? NormalizeIso2(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        return normalized.Length == 2 && normalized.All(IsAsciiLetter)
            ? normalized
            : null;
    }

    public static string? NormalizeIso3(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        return normalized.Length == 3 && normalized.All(IsAsciiLetter)
            ? normalized
            : null;
    }

    public static string? NormalizeIso3ForBoundaryImport(string? value)
    {
        if (IsKosovoIsoAlias(value))
            return NormalizeIso2(value) is not null ? KosovoGeoBoundariesIso3 : value!.Trim().ToUpperInvariant();

        return NormalizeIso3(value);
    }

    public static string? ToIso2ForBoundaryImport(string? iso2OrIso3)
    {
        if (IsKosovoIsoAlias(iso2OrIso3))
            return KosovoIso2;

        return ToIso2(iso2OrIso3);
    }

    public static IReadOnlyList<string> BoundaryImportIsoAliases(string? value)
    {
        if (IsKosovoIsoAlias(value))
            return KosovoIsoAliases;

        var normalized = NormalizeIso3(value);
        return normalized is null ? Array.Empty<string>() : new[] { normalized };
    }

    public static bool AreBoundaryImportIsoAliases(string? first, string? second) =>
        IsKosovoIsoAlias(first) && IsKosovoIsoAlias(second);

    public static bool IsKosovoIsoAlias(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        return !string.IsNullOrWhiteSpace(normalized) &&
            KosovoIsoAliases.Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsKosovoName(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        return !string.IsNullOrWhiteSpace(normalized) &&
            KosovoNames.Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    public static string? ToIso3(string? iso2OrIso3)
    {
        var iso3 = NormalizeIso3(iso2OrIso3);
        if (iso3 is not null)
            return iso3;

        return FindByIso2(iso2OrIso3)?.Iso3;
    }

    public static string? ToIso2(string? iso2OrIso3)
    {
        var iso2 = NormalizeIso2(iso2OrIso3);
        if (iso2 is not null)
            return iso2;

        return FindByIso3(iso2OrIso3)?.Iso2;
    }

    private static IReadOnlyList<CountryRegistryItem> BuildCountries()
    {
        return CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .Select(culture =>
            {
                try
                {
                    return new RegionInfo(culture.Name);
                }
                catch (CultureNotFoundException)
                {
                    return null;
                }
            })
            .Where(region => region is not null)
            .Select(region => region!)
            .Where(region =>
                NormalizeIso2(region.TwoLetterISORegionName) is not null &&
                NormalizeIso3(region.ThreeLetterISORegionName) is not null)
            .GroupBy(region => region.TwoLetterISORegionName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var region = group.First();
                return new CountryRegistryItem(
                    region.TwoLetterISORegionName.ToUpperInvariant(),
                    region.ThreeLetterISORegionName.ToUpperInvariant(),
                    region.EnglishName);
            })
            .OrderBy(country => country.Name)
            .ThenBy(country => country.Iso2)
            .ToList();
    }

    private static bool IsAsciiLetter(char value) =>
        value is >= 'A' and <= 'Z';
}

internal sealed record CountryRegistryItem(string Iso2, string Iso3, string Name);
