using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Application.DTOs.Auth;

public class LoginResponseDto
{
    /// <summary>Short-lived JWT (15 minutes).</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Long-lived opaque refresh token (7 days for remembered sessions).</summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>Access token lifetime in seconds (900 = 15 minutes).</summary>
    public int ExpiresIn { get; set; } = 900;
}