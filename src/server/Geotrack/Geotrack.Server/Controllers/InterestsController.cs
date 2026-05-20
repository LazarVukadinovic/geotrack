using Geotrack.Application.DTOs.Interests;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/interests")]
public class InterestsController : ControllerBase
{
    private readonly IInterestTopicService _interestTopicService;

    public InterestsController(IInterestTopicService interestTopicService)
    {
        _interestTopicService = interestTopicService;
    }

    [HttpGet]
    [HttpGet("onboarding")]
    public async Task<ActionResult<List<InterestTopicGroupDto>>> GetOnboardingTopics()
    {
        return Ok(await _interestTopicService.GetOnboardingTopicsAsync());
    }
}
