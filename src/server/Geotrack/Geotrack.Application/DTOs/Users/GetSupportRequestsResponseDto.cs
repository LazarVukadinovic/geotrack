namespace Geotrack.Application.DTOs.Users;

public class GetSupportRequestsResponseDto
{
    public List<SupportRequestItemDto> Requests { get; set; } = new();
}
