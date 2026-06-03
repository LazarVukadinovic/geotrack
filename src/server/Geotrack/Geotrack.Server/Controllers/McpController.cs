using System.Text.Json;
using Geotrack.Application.DTOs.GeoTrackTools;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Options;
using Geotrack.Application.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/mcp")]
[EnableRateLimiting("mcp")]
public class McpController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IGeoTrackToolService _toolService;
    private readonly McpOptions _options;
    private readonly ILogger<McpController> _logger;

    public McpController(
        IGeoTrackToolService toolService,
        IOptions<McpOptions> options,
        ILogger<McpController> logger)
    {
        _toolService = toolService;
        _options = options.Value;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult GetStatus()
    {
        var unavailable = RejectUnavailableOrUnauthorized();
        if (unavailable is not null)
        {
            return unavailable;
        }

        if (AcceptsEventStream())
        {
            Response.Headers.Allow = HttpMethods.Post;
            Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return new EmptyResult();
        }

        return Ok(new
        {
            name = "geotrack-mcp",
            transport = "remote-http",
            readOnly = true,
            tools = BuildTools().Select(tool => tool.Name).ToArray()
        });
    }

    [HttpPost]
    public async Task<IActionResult> Handle([FromBody] JsonElement request, CancellationToken cancellationToken)
    {
        var unavailable = RejectUnavailableOrUnauthorized();
        if (unavailable is not null)
        {
            return unavailable;
        }

        if (request.ValueKind == JsonValueKind.Array)
        {
            return BadRequest(new { message = "JSON-RPC batch requests are not supported by GeoTrack MCP." });
        }

        if (request.ValueKind != JsonValueKind.Object)
        {
            return BadRequest(new { message = "JSON-RPC payload must be an object." });
        }

        var hasId = request.TryGetProperty("id", out var idElement);
        var id = hasId ? idElement.Clone() : (JsonElement?)null;
        var method = request.TryGetProperty("method", out var methodElement) ? methodElement.GetString() : null;
        if (!hasId)
        {
            return AcceptNotification(method);
        }

        if (string.IsNullOrWhiteSpace(method))
        {
            return Ok(Error(id, -32600, "Invalid JSON-RPC request."));
        }

        try
        {
            return method switch
            {
                "initialize" => Ok(Result(id, new
                {
                    protocolVersion = "2025-06-18",
                    capabilities = new
                    {
                        tools = new { listChanged = false },
                        resources = new { listChanged = false }
                    },
                    serverInfo = new { name = "geotrack-mcp", version = "0.1.0" }
                })),
                "ping" => Ok(Result(id, new { })),
                "tools/list" => Ok(Result(id, new { tools = BuildTools() })),
                "resources/list" => Ok(Result(id, new { resources = Array.Empty<object>() })),
                "tools/call" => Ok(Result(id, await CallToolAsync(GetParams(request), cancellationToken))),
                _ => Ok(Error(id, -32601, "Method not found."))
            };
        }
        catch (AgentException ex)
        {
            return Ok(Error(id, -32602, ex.SafeMessage));
        }
        catch (Exception ex)
        {
            _logger.LogWarning("MCP method {Method} failed with {ExceptionType}.", method, ex.GetType().Name);
            return Ok(Error(id, -32603, "GeoTrack MCP tool execution failed."));
        }
    }

    private IActionResult AcceptNotification(string? method)
    {
        if (!string.IsNullOrWhiteSpace(method) &&
            !string.Equals(method, "notifications/initialized", StringComparison.Ordinal))
        {
            _logger.LogDebug("Accepted MCP notification {Method}.", method);
        }

        return StatusCode(StatusCodes.Status202Accepted);
    }

    private IActionResult? RejectUnavailableOrUnauthorized()
    {
        if (!_options.Enabled)
        {
            return NotFound(new { message = "GeoTrack MCP is disabled." });
        }

        var isAuthenticated = User.Identity?.IsAuthenticated == true;
        if (!isAuthenticated && (_options.RequireAuth || !_options.AllowAnonymousPublicTools))
        {
            return Unauthorized(new { message = "GeoTrack MCP requires authentication." });
        }

        return null;
    }

    private bool AcceptsEventStream() =>
        Request.Headers.Accept.Any(value =>
            value?.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase) == true);

    private async Task<object> CallToolAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var name = parameters.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
        var args = parameters.TryGetProperty("arguments", out var argsElement)
            ? argsElement
            : JsonDocument.Parse("{}").RootElement;

        object? payload = name switch
        {
            "geotrack_search_places" => await _toolService.SearchPlacesAsync(
                new GeoTrackToolSearchRequest
                {
                    Query = ReadString(args, "query", required: true),
                    Location = ReadString(args, "location", required: false),
                    DestinationId = ReadOptionalPositiveInt(args, "destinationId"),
                    Tags = ReadOptionalStringList(args, "tags"),
                    Categories = ReadOptionalStringList(args, "categories"),
                    PlaceTypes = ReadOptionalStringList(args, "placeTypes"),
                    Limit = ReadOptionalInt(args, "limit")
                },
                cancellationToken),
            "geotrack_get_place_details" => await _toolService.GetPlaceDetailsAsync(
                ReadPositiveInt(args, "id"),
                cancellationToken),
            "geotrack_search_stays" => await _toolService.SearchStaysAsync(
                new GeoTrackToolSearchRequest
                {
                    Query = ReadString(args, "query", required: true),
                    Location = ReadString(args, "location", required: false),
                    DestinationId = ReadOptionalPositiveInt(args, "destinationId"),
                    Tags = ReadOptionalStringList(args, "tags"),
                    Categories = ReadOptionalStringList(args, "categories"),
                    StayTypes = ReadOptionalStringList(args, "stayTypes"),
                    Limit = ReadOptionalInt(args, "limit")
                },
                cancellationToken),
            "geotrack_get_stay_details" => await _toolService.GetStayDetailsAsync(
                ReadPositiveInt(args, "id"),
                cancellationToken),
            "geotrack_search_events" => await _toolService.SearchEventsAsync(
                new GeoTrackToolSearchRequest
                {
                    Query = ReadString(args, "query", required: true),
                    Location = ReadString(args, "location", required: false),
                    DestinationId = ReadOptionalPositiveInt(args, "destinationId"),
                    Tags = ReadOptionalStringList(args, "tags"),
                    Categories = ReadOptionalStringList(args, "categories"),
                    EventCategories = ReadOptionalStringList(args, "eventCategories"),
                    Limit = ReadOptionalInt(args, "limit"),
                    UpcomingOnly = ReadOptionalBool(args, "upcomingOnly") ?? true
                },
                cancellationToken),
            "geotrack_get_event_details" => await _toolService.GetEventDetailsAsync(
                ReadPositiveInt(args, "id"),
                cancellationToken),
            "geotrack_get_map_filters" => await _toolService.GetMapFiltersAsync(
                new GeoTrackMapFiltersRequest
                {
                    North = ReadDouble(args, "north"),
                    South = ReadDouble(args, "south"),
                    East = ReadDouble(args, "east"),
                    West = ReadDouble(args, "west"),
                    Zoom = ReadOptionalDouble(args, "zoom") ?? 13d
                },
                cancellationToken),
            "geotrack_resolve_area" => await _toolService.ResolveAreaAsync(
                new GeoTrackAreaResolveRequest
                {
                    Query = ReadString(args, "query", required: true),
                    Limit = ReadOptionalLimit(args, "limit", 10)
                },
                cancellationToken),
            "geotrack_search_catalog" => await _toolService.SearchCatalogAsync(
                new GeoTrackCatalogSearchRequest
                {
                    Query = ReadString(args, "query", required: false),
                    AreaType = ReadString(args, "areaType", required: false),
                    AreaId = ReadOptionalPositiveInt(args, "areaId"),
                    Category = ReadString(args, "category", required: false),
                    EntityTypes = ReadOptionalStringList(args, "entityTypes"),
                    Limit = ReadOptionalLimit(args, "limit", 20)
                },
                cancellationToken),
            "geotrack_search_by_area" => await _toolService.SearchByAreaAsync(
                new GeoTrackAreaContentRequest
                {
                    AreaType = ReadString(args, "areaType", required: true),
                    AreaId = ReadPositiveInt(args, "areaId"),
                    Category = ReadString(args, "category", required: false),
                    EntityTypes = ReadOptionalStringList(args, "entityTypes"),
                    Limit = ReadOptionalLimit(args, "limit", 20)
                },
                cancellationToken),
            "geotrack_get_area_overview" => await _toolService.GetAreaOverviewAsync(
                new GeoTrackAreaOverviewRequest
                {
                    AreaType = ReadString(args, "areaType", required: true),
                    AreaId = ReadPositiveInt(args, "areaId"),
                    LimitPerType = ReadOptionalLimit(args, "limitPerType", 10)
                },
                cancellationToken),
            "geotrack_get_category_options" => await _toolService.GetCategoryOptionsAsync(
                new GeoTrackCategoryOptionsRequest
                {
                    AreaType = ReadString(args, "areaType", required: false),
                    AreaId = ReadOptionalPositiveInt(args, "areaId"),
                    CategoryGroup = ReadString(args, "categoryGroup", required: false)
                },
                cancellationToken),
            _ => throw new AgentException("tool_not_allowed", "The requested MCP tool is not allowed.", 400)
        };

        return new
        {
            content = new[]
            {
                new
                {
                    type = "text",
                    text = JsonSerializer.Serialize(payload, JsonOptions)
                }
            },
            isError = false
        };
    }

    private static JsonElement GetParams(JsonElement request)
    {
        if (!request.TryGetProperty("params", out var parameters) ||
            parameters.ValueKind != JsonValueKind.Object)
        {
            throw new AgentException("invalid_tool_arguments", "Tool parameters are required.", 400);
        }

        return parameters;
    }

    private static string ReadString(JsonElement args, string name, bool required)
    {
        if (!args.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
        {
            if (required)
            {
                throw new AgentException("invalid_tool_arguments", $"{name} is required.", 400);
            }

            return string.Empty;
        }

        var value = element.GetString()?.Trim() ?? string.Empty;
        if ((required && value.Length < 2) || value.Length > 100 || value.Any(char.IsControl))
        {
            throw new AgentException("invalid_tool_arguments", $"{name} is invalid.", 400);
        }

        return value;
    }

    private static int ReadPositiveInt(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var element) ||
            element.ValueKind != JsonValueKind.Number ||
            !element.TryGetInt32(out var value) ||
            value <= 0)
        {
            throw new AgentException("invalid_tool_arguments", $"{name} must be a positive integer.", 400);
        }

        return value;
    }

    private static int? ReadOptionalInt(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Number ||
            !element.TryGetInt32(out var value) ||
            value <= 0 ||
            value > 10)
        {
            throw new AgentException("invalid_tool_arguments", $"{name} must be between 1 and 10.", 400);
        }

        return value;
    }

    private static int? ReadOptionalLimit(JsonElement args, string name, int maximum)
    {
        if (!args.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Number ||
            !element.TryGetInt32(out var value) ||
            value <= 0 ||
            value > maximum)
        {
            throw new AgentException("invalid_tool_arguments", $"{name} must be between 1 and {maximum}.", 400);
        }

        return value;
    }

    private static int? ReadOptionalPositiveInt(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Number ||
            !element.TryGetInt32(out var value) ||
            value <= 0)
        {
            throw new AgentException("invalid_tool_arguments", $"{name} must be a positive integer.", 400);
        }

        return value;
    }

    private static List<string> ReadOptionalStringList(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return new List<string>();
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new AgentException("invalid_tool_arguments", $"{name} must be an array.", 400);
        }

        var values = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new AgentException("invalid_tool_arguments", $"{name} must contain strings.", 400);
            }

            var value = item.GetString()?.Trim() ?? string.Empty;
            if (value.Length is < 1 or > 50 || value.Any(char.IsControl))
            {
                throw new AgentException("invalid_tool_arguments", $"{name} contains an invalid value.", 400);
            }

            values.Add(value);
            if (values.Count > 8)
            {
                throw new AgentException("invalid_tool_arguments", $"{name} may contain at most 8 values.", 400);
            }
        }

        return values;
    }

    private static bool? ReadOptionalBool(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new AgentException("invalid_tool_arguments", $"{name} must be a boolean.", 400);
        }

        return element.GetBoolean();
    }

    private static double ReadDouble(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var element) ||
            element.ValueKind != JsonValueKind.Number ||
            !element.TryGetDouble(out var value) ||
            double.IsNaN(value) ||
            double.IsInfinity(value))
        {
            throw new AgentException("invalid_tool_arguments", $"{name} must be a finite number.", 400);
        }

        return value;
    }

    private static double? ReadOptionalDouble(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return ReadDouble(args, name);
    }

    private static object Result(JsonElement? id, object result) => new
    {
        jsonrpc = "2.0",
        id = id ?? JsonDocument.Parse("null").RootElement,
        result
    };

    private static object Error(JsonElement? id, int code, string message) => new
    {
        jsonrpc = "2.0",
        id = id ?? JsonDocument.Parse("null").RootElement,
        error = new { code, message }
    };

    private static List<McpToolDefinition> BuildTools() => new()
    {
        SearchTool("geotrack_search_places", "Search public GeoTrack places. Read-only and capped at 10 results."),
        IdTool("geotrack_get_place_details", "Get safe public details for one GeoTrack place."),
        SearchTool("geotrack_search_stays", "Search public GeoTrack stays. Read-only and capped at 10 results."),
        IdTool("geotrack_get_stay_details", "Get safe public details for one GeoTrack stay."),
        SearchTool("geotrack_search_events", "Search public GeoTrack events. Read-only and capped at 10 results.", includeUpcoming: true),
        IdTool("geotrack_get_event_details", "Get safe public details for one GeoTrack event."),
        new()
        {
            Name = "geotrack_resolve_area",
            Description = "Resolve a user area phrase to public GeoTrack destinations, locations, regions, territories, places, or events. Read-only.",
            InputSchema = new
            {
                type = "object",
                additionalProperties = false,
                required = new[] { "query" },
                properties = new Dictionary<string, object>
                {
                    ["query"] = new { type = "string", minLength = 2, maxLength = 100 },
                    ["limit"] = new { type = "integer", minimum = 1, maximum = 10 }
                }
            }
        },
        new()
        {
            Name = "geotrack_search_catalog",
            Description = "Unified search over public GeoTrack catalog content. Read-only and capped at 20 results.",
            InputSchema = new
            {
                type = "object",
                additionalProperties = false,
                properties = CatalogSearchProperties()
            }
        },
        new()
        {
            Name = "geotrack_search_by_area",
            Description = "Return grouped public catalog content inside a resolved area. Read-only and capped at 20 results per request.",
            InputSchema = new
            {
                type = "object",
                additionalProperties = false,
                required = new[] { "areaType", "areaId" },
                properties = AreaScopedProperties(includeLimit: true)
            }
        },
        new()
        {
            Name = "geotrack_get_area_overview",
            Description = "Summarize public GeoTrack content counts and top results for a resolved area. Read-only.",
            InputSchema = new
            {
                type = "object",
                additionalProperties = false,
                required = new[] { "areaType", "areaId" },
                properties = new Dictionary<string, object>
                {
                    ["areaType"] = AreaTypeSchema(),
                    ["areaId"] = new { type = "integer", minimum = 1 },
                    ["limitPerType"] = new { type = "integer", minimum = 1, maximum = 10 }
                }
            }
        },
        new()
        {
            Name = "geotrack_get_category_options",
            Description = "Return known public category groups, tags, and type names Ask Agent can use. Read-only.",
            InputSchema = new
            {
                type = "object",
                additionalProperties = false,
                properties = new Dictionary<string, object>
                {
                    ["areaType"] = AreaTypeSchema(),
                    ["areaId"] = new { type = "integer", minimum = 1 },
                    ["categoryGroup"] = new { type = "string", minLength = 2, maxLength = 50 }
                }
            }
        },
        new()
        {
            Name = "geotrack_get_map_filters",
            Description = "Get bounded public map category counts for a validated viewport. Read-only.",
            InputSchema = new
            {
                type = "object",
                additionalProperties = false,
                required = new[] { "north", "south", "east", "west" },
                properties = new Dictionary<string, object>
                {
                    ["north"] = NumberSchema(-90, 90),
                    ["south"] = NumberSchema(-90, 90),
                    ["east"] = NumberSchema(-180, 180),
                    ["west"] = NumberSchema(-180, 180),
                    ["zoom"] = NumberSchema(0, 22)
                }
            }
        }
    };

    private static McpToolDefinition SearchTool(string name, string description, bool includeUpcoming = false)
    {
        var properties = new Dictionary<string, object>
        {
            ["query"] = new { type = "string", minLength = 2, maxLength = 100 },
            ["location"] = new { type = "string", minLength = 2, maxLength = 100 },
            ["destinationId"] = new { type = "integer", minimum = 1, maximum = int.MaxValue },
            ["tags"] = StringArraySchema(),
            ["categories"] = StringArraySchema(),
            ["limit"] = new { type = "integer", minimum = 1, maximum = 10 }
        };

        if (name == "geotrack_search_places")
        {
            properties["placeTypes"] = StringArraySchema();
        }

        if (name == "geotrack_search_stays")
        {
            properties["stayTypes"] = StringArraySchema();
        }

        if (includeUpcoming)
        {
            properties["eventCategories"] = StringArraySchema();
            properties["upcomingOnly"] = new { type = "boolean" };
        }

        return new McpToolDefinition
        {
            Name = name,
            Description = description,
            InputSchema = new
            {
                type = "object",
                additionalProperties = false,
                required = new[] { "query" },
                properties
            }
        };
    }

    private static McpToolDefinition IdTool(string name, string description) => new()
    {
        Name = name,
        Description = description,
        InputSchema = new
        {
            type = "object",
            additionalProperties = false,
            required = new[] { "id" },
            properties = new Dictionary<string, object>
            {
                ["id"] = new { type = "integer", minimum = 1 }
            }
        }
    };

    private static object NumberSchema(double minimum, double maximum) => new
    {
        type = "number",
        minimum,
        maximum
    };

    private static Dictionary<string, object> CatalogSearchProperties()
    {
        var properties = AreaScopedProperties(includeLimit: true);
        properties["query"] = new { type = "string", minLength = 2, maxLength = 100 };
        return properties;
    }

    private static Dictionary<string, object> AreaScopedProperties(bool includeLimit) => new()
    {
        ["areaType"] = AreaTypeSchema(),
        ["areaId"] = new { type = "integer", minimum = 1 },
        ["category"] = new { type = "string", minLength = 2, maxLength = 50 },
        ["entityTypes"] = StringArraySchema(),
        ["limit"] = includeLimit
            ? new { type = "integer", minimum = 1, maximum = 20 }
            : new { type = "integer", minimum = 1, maximum = 10 }
    };

    private static object AreaTypeSchema() => new
    {
        type = "string",
        @enum = new[] { "destination", "location", "region", "territory", "place", "stay", "event" }
    };

    private static object StringArraySchema() => new
    {
        type = "array",
        maxItems = 8,
        items = new { type = "string", minLength = 1, maxLength = 50 }
    };

    private sealed class McpToolDefinition
    {
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("inputSchema")]
        public object InputSchema { get; set; } = new();
    }
}
