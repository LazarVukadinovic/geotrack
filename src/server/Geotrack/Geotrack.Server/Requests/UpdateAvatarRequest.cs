using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Requests;

public class UpdateAvatarRequest
{
    [FromForm(Name = "avatar")]
    [JsonPropertyName("avatar")]
    public IFormFile? Avatar { get; set; }
}
