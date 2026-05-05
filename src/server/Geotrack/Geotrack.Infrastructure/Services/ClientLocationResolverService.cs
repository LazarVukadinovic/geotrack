using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Geotrack.Application.DTOs.Locations;
using Geotrack.Application.Interfaces.Service;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Geotrack.Infrastructure.Services;

public class ClientLocationResolverService : IClientLocationResolverService
{
    private static readonly TimeSpan ApproximateLocationCacheDuration = TimeSpan.FromMinutes(15);

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ClientLocationResolverService> _logger;

    public ClientLocationResolverService(
        HttpClient httpClient,
        IMemoryCache cache,
        ILogger<ClientLocationResolverService> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _logger = logger;
    }

    public async Task<ApproximateLocationDto?> ResolveApproximateLocationAsync(string? clientIpAddress, CancellationToken cancellationToken = default)
    {
        var cacheKey = BuildCacheKey(clientIpAddress);
        if (_cache.TryGetValue<ApproximateLocationDto>(cacheKey, out var cachedLocation))
        {
            return cachedLocation;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            BuildEndpoint(clientIpAddress));

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (IsProviderFailure(ex, cancellationToken))
        {
            LogProviderFailure(ex);
            return null;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            IpWhoIsResponse? payload;
            try
            {
                payload = await response.Content.ReadFromJsonAsync<IpWhoIsResponse>(cancellationToken: cancellationToken);
            }
            catch (Exception ex) when (IsProviderFailure(ex, cancellationToken))
            {
                LogProviderFailure(ex);
                return null;
            }

            if (payload is null || !payload.Success || !payload.Latitude.HasValue || !payload.Longitude.HasValue)
            {
                return null;
            }

            if (!HasValidCoordinates(payload.Latitude.Value, payload.Longitude.Value))
            {
                return null;
            }

            var city = payload.City ?? string.Empty;
            var region = payload.Region ?? string.Empty;
            var country = payload.Country ?? string.Empty;

            var location = new ApproximateLocationDto
            {
                Latitude = payload.Latitude.Value,
                Longitude = payload.Longitude.Value,
                City = city,
                Region = region,
                Country = country,
                DisplayLabel = BuildDisplayLabel(city, region, country),
                Source = "ip"
            };

            _cache.Set(
                cacheKey,
                location,
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = ApproximateLocationCacheDuration
                });

            return location;
        }
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

    private void LogProviderFailure(Exception exception)
    {
        _logger.LogWarning(
            "Approximate location provider request failed with {ExceptionType}.",
            exception.GetType().Name);
    }

    private static bool IsProviderFailure(Exception exception, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested &&
        (exception is HttpRequestException ||
         exception is TaskCanceledException ||
         exception is JsonException ||
         exception is NotSupportedException);

    private static bool HasValidCoordinates(double latitude, double longitude)
    {
        return !double.IsNaN(latitude) &&
               !double.IsNaN(longitude) &&
               !double.IsInfinity(latitude) &&
               !double.IsInfinity(longitude) &&
               latitude >= -90 &&
               latitude <= 90 &&
               longitude >= -180 &&
               longitude <= 180 &&
               (latitude != 0d || longitude != 0d);
    }

    private static string BuildCacheKey(string? clientIpAddress)
    {
        var normalized = NormalizeCacheAddress(clientIpAddress);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return $"location:approximate:v1:{Convert.ToHexString(hash)}";
    }

    private static string NormalizeCacheAddress(string? clientIpAddress)
    {
        var trimmed = clientIpAddress?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || !IPAddress.TryParse(trimmed, out var address) || !IsPublicIpAddress(trimmed))
        {
            return "provider-self";
        }

        return address.ToString();
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
