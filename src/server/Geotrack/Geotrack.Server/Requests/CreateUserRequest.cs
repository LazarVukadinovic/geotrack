namespace Geotrack.Server.Requests;

public class CreateUserRequest
{
    public string Username { get; set; }
    public string Email { get; set; }
    public List<int> RoleIds { get; set; }
}