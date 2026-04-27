namespace Geotrack.Application.DTOs.Users;

public class InferredInterestItemDto : InterestItemDto
{
    public double Score { get; set; }
    public DateTime LastInteractionAt { get; set; }
    public string? LastReason { get; set; }
}
