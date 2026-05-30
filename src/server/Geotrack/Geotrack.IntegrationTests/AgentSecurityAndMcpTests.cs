using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Geotrack.Application.DTOs.Agent;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Infrastructure;
using Geotrack.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geotrack.IntegrationTests;

public class AgentSecurityAndMcpTests
{
    private static readonly string[] AllowedMcpTools =
    [
        "geotrack_search_places",
        "geotrack_get_place_details",
        "geotrack_search_stays",
        "geotrack_get_stay_details",
        "geotrack_search_events",
        "geotrack_get_event_details",
        "geotrack_get_map_filters"
    ];

    [Fact]
    public async Task Provider_metadata_never_returns_api_key_values()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        var response = await client.GetAsync("/api/agent/providers");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"providers\"", body);
        Assert.DoesNotContain("apiKey", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EncryptedApiKey", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Legacy_user_api_key_endpoints_are_not_available()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var (client, _) = await RegisterFreshUserAsync(app);

        var list = await client.GetAsync("/api/agent/api-keys");
        var save = await client.PostAsJsonAsync(
            "/api/agent/api-keys",
            new { provider = "gemini", apiKey = "test-gemini-secret-1234" });
        var test = await client.PostAsJsonAsync(
            "/api/agent/api-keys/test",
            new { provider = "gemini", apiKey = "test-gemini-secret-1234" });
        var delete = await client.DeleteAsync("/api/agent/api-keys/gemini");

        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, save.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, test.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    [Fact]
    public async Task Chat_without_gemini_keys_returns_safe_unavailable_response()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var (client, _) = await RegisterFreshUserAsync(app);

        var response = await client.PostAsJsonAsync(
            "/api/agent/chat",
            new AgentChatRequestDto
            {
                Message = "Find restaurants"
            });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(error);
        Assert.Equal("agent_unavailable", error!["code"]);
        Assert.Equal("Ask Agent is currently unavailable. Please try again later.", error["message"]);
    }

    [Fact]
    public async Task Chat_rejects_client_provider_model_and_key_fields()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var (client, _) = await RegisterFreshUserAsync(app);

        var response = await client.PostAsJsonAsync(
            "/api/agent/chat",
            new
            {
                message = "Find restaurants",
                provider = "gemini",
                model = "gemini-2.5-flash",
                keyMode = "session",
                sessionApiKey = "user-supplied-key"
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var error = JsonSerializer.Deserialize<Dictionary<string, string>>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(error);
        Assert.Equal("invalid_request", error!["code"]);
        Assert.DoesNotContain("user-supplied-key", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mcp_initialize_and_initialized_notification_match_streamable_http_handshake()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        var initialize = await PostMcpJsonAsync(
            client,
            new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2025-06-18",
                    capabilities = new { },
                    clientInfo = new { name = "integration-test", version = "0.0.1" }
                }
            });

        Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);
        Assert.False(initialize.Headers.Contains("Mcp-Session-Id"));
        using (var document = JsonDocument.Parse(await initialize.Content.ReadAsStringAsync()))
        {
            var root = document.RootElement;
            Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
            Assert.Equal(1, root.GetProperty("id").GetInt32());

            var result = root.GetProperty("result");
            Assert.Equal("2025-06-18", result.GetProperty("protocolVersion").GetString());
            Assert.Equal("geotrack-mcp", result.GetProperty("serverInfo").GetProperty("name").GetString());
            Assert.Equal("0.1.0", result.GetProperty("serverInfo").GetProperty("version").GetString());
            Assert.True(result.GetProperty("capabilities").TryGetProperty("tools", out _));
        }

        var initialized = await PostMcpJsonAsync(
            client,
            new
            {
                jsonrpc = "2.0",
                method = "notifications/initialized",
                @params = new { }
            });

        Assert.Equal(HttpStatusCode.Accepted, initialized.StatusCode);
        Assert.False(initialized.Headers.Contains("Mcp-Session-Id"));
        Assert.Equal(string.Empty, await initialized.Content.ReadAsStringAsync());

        var unknownNotification = await PostMcpJsonAsync(
            client,
            new
            {
                jsonrpc = "2.0",
                method = "notifications/cancelled",
                @params = new { requestId = 1 }
            });

        Assert.Equal(HttpStatusCode.Accepted, unknownNotification.StatusCode);
        Assert.Equal(string.Empty, await unknownNotification.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Mcp_tool_list_exposes_only_read_only_allowlisted_tools()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        var response = await PostMcpJsonAsync(
            client,
            new { jsonrpc = "2.0", id = 1, method = "tools/list" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var toolElements = document.RootElement
            .GetProperty("result")
            .GetProperty("tools")
            .EnumerateArray()
            .ToList();
        var tools = toolElements
            .Select(tool => tool.GetProperty("name").GetString() ?? string.Empty)
            .ToList();

        Assert.Equal(AllowedMcpTools, tools);

        Assert.All(tools, name =>
        {
            Assert.DoesNotContain("create", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("update", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("delete", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("approve", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("reject", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("admin", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sql", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("user", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("collection", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("profile", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("key", name, StringComparison.OrdinalIgnoreCase);
        });

        Assert.All(toolElements, tool =>
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.GetProperty("name").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(tool.GetProperty("description").GetString()));

            var inputSchema = tool.GetProperty("inputSchema");
            Assert.Equal("object", inputSchema.GetProperty("type").GetString());
            Assert.True(inputSchema.TryGetProperty("properties", out var properties));
            Assert.Equal(JsonValueKind.Object, properties.ValueKind);
        });
    }

    [Fact]
    public async Task Mcp_tools_call_allows_public_read_only_tool_and_rejects_invalid_names()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        var valid = await PostMcpJsonAsync(
            client,
            new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/call",
                @params = new
                {
                    name = "geotrack_search_places",
                    arguments = new { query = "Banje Beach", location = "Dubrovnik", limit = 1 }
                }
            });

        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        using (var document = JsonDocument.Parse(await valid.Content.ReadAsStringAsync()))
        {
            var result = document.RootElement.GetProperty("result");
            Assert.False(result.GetProperty("isError").GetBoolean());
            var content = Assert.Single(result.GetProperty("content").EnumerateArray());
            Assert.Equal("text", content.GetProperty("type").GetString());

            using var payload = JsonDocument.Parse(content.GetProperty("text").GetString() ?? "[]");
            var item = Assert.Single(payload.RootElement.EnumerateArray());
            Assert.Equal("place", item.GetProperty("entityType").GetString());
            Assert.Equal("Banje Beach", item.GetProperty("title").GetString());
            Assert.Equal("/places/" + item.GetProperty("id").GetInt32(), item.GetProperty("detailRoute").GetString());
        }

        var invalid = await PostMcpJsonAsync(
            client,
            new
            {
                jsonrpc = "2.0",
                id = 3,
                method = "tools/call",
                @params = new
                {
                    name = "geotrack_delete_place",
                    arguments = new { id = 1 }
                }
            });

        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        var invalidBody = await invalid.Content.ReadAsStringAsync();
        using var invalidDocument = JsonDocument.Parse(invalidBody);
        var error = invalidDocument.RootElement.GetProperty("error");
        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Contains("not allowed", error.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", invalidBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiKey", invalidBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mcp_rejects_invalid_tool_arguments()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        var response = await PostMcpJsonAsync(
            client,
            new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/call",
                @params = new
                {
                    name = "geotrack_search_places",
                    arguments = new { query = "a", limit = 20 }
                }
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var error = document.RootElement.GetProperty("error");
        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Contains("invalid", error.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiKey", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mcp_get_with_event_stream_accept_does_not_return_debug_metadata()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/mcp");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("geotrack-mcp", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tools", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mcp_batch_payload_is_rejected_clearly()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        var response = await PostMcpJsonAsync(
            client,
            new[]
            {
                new { jsonrpc = "2.0", id = 1, method = "tools/list" }
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("batch", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(HttpClient client, int userId)> RegisterFreshUserAsync(GeotrackApiFactory app)
    {
        var client = app.CreateClient();
        var email = $"agent-{Guid.NewGuid():N}@example.com";
        var register = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequestDto
            {
                FullName = "Agent Tester",
                Email = email,
                Password = "Password123!"
            });
        register.EnsureSuccessStatusCode();

        var session = await register.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(session);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.AccessToken);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await db.Users.Where(user => user.Email == email).Select(user => user.Id).SingleAsync();

        return (client, userId);
    }

    private static async Task<HttpResponseMessage> PostMcpJsonAsync(HttpClient client, object payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/mcp")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        return await client.SendAsync(request);
    }
}
