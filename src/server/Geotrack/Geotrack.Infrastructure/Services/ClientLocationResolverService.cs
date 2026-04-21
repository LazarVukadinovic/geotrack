using System.Net;
using System.Net.Http.Json;
using Geotrack.Application.DTOs.Locations;
using Geotrack.Application.Interfaces.Service;

namespace Geotrack.Infrastructure.Services;

public class ClientLocationResolverService : IClientLocationResolverService
{
    private readonly HttpClient _httpClient;

    public ClientLocationResolverService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ApproximateLocationDto?> ResolveApproximateLocationAsync(string? clientIpAddress, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            BuildEndpoint(clientIpAddress));
        request.Headers.UserAgent.ParseAdd("GeoTrack/1.0");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<IpWhoIsResponse>(cancellationToken: cancellationToken);
        if (payload is null || !payload.Success || !payload.Latitude.HasValue || !payload.Longitude.HasValue)
        {
            return null;
        }

        var city = payload.City ?? string.Empty;
        var region = payload.Region ?? string.Empty;
        var country = payload.Country ?? string.Empty;

        return new ApproximateLocationDto
        {
            Latitude = payload.Latitude.Value,
            Longitude = payload.Longitude.Value,
            City = city,
            Region = region,
            Country = country,
            DisplayLabel = BuildDisplayLabel(city, region, country),
            Source = "ip"
        };
    }

    private static string BuildEndpoint(string? clientIpAddress)
    {
        var trimmed = clientIpAddress?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || !IsPublicIpAddress(trimmed))
        {
            return "https://ipwho.is/";
        }

        return $"https://ipwho.is/{Uri.EscapeDataString(trimmed)}";
    }

    private static string BuildDisplayLabel(string city, string region, string country)
    {
        if (!string.IsNullOrWhiteSpace(city) && !string.IsNullOrWhiteSpace(country))
        {
            return $"{city}, {country}";
        }

        if (!string.IsNullOrWhiteSpace(city))
        {
            return city;
        }

        if (!string.IsNullOrWhiteSpace(region) && !string.IsNullOrWhiteSpace(country))
        {
            return $"{region}, {country}";
        }

        if (!string.IsNullOrWhiteSpace(region))
        {
            return region;
        }

        return string.IsNullOrWhiteSpace(country) ? "Explore" : country;
    }

    private static bool IsPublicIpAddress(string ipAddress)
    {
        if (!IPAddress.TryParse(ipAddress, out var address) || IPAddress.IsLoopback(address))
        {
            return false;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return !(bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168));
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast
                || address.ToString().StartsWith("fd", StringComparison.OrdinalIgnoreCase)
                || address.ToString().StartsWith("fc", StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }

    private sealed class IpWhoIsResponse
    {
        public bool Success { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? City { get; set; }
        public string? Region { get; set; }
        public string? Country { get; set; }
    }
}
