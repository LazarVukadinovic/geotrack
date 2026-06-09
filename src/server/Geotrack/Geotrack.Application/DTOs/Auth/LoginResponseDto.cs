using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Application.DTOs.Auth;

public class LoginResponseDto
{
    /// <summary>Short-lived JWT access token.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Refresh tokens are now transported via an HttpOnly cookie.
    /// This field remains for compatibility and is intentionally blank.
    /// </summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>Access token lifetime in seconds.</summary>
    public int ExpiresIn { get; set; } = 3600;

    public bool SessionPersistent { get; set; }
    public AuthSessionDto? Session { get; set; }
}
