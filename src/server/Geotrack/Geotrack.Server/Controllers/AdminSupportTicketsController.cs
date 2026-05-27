using System.Security.Claims;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/admin/support-tickets")]
[Authorize(Policy = "RequireAdmin")]
public sealed class AdminSupportTicketsController : ControllerBase
{
    private readonly IAdminSupportTicketService _supportTickets;
    private readonly ILogger<AdminSupportTicketsController> _logger;

    public AdminSupportTicketsController(
        IAdminSupportTicketService supportTickets,
        ILogger<AdminSupportTicketsController> logger)
    {
        _supportTickets = supportTickets;
        _logger = logger;
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<AdminSupportTicketListResponseDto>> GetTickets(
        [FromQuery] AdminSupportTicketListRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _supportTickets.GetTicketsAsync(GetCurrentUserId(), request, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("{ticketId:int}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<AdminSupportTicketDetailDto>> GetTicket(
        [FromRoute] int ticketId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _supportTickets.GetTicketAsync(GetCurrentUserId(), ticketId, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("{ticketId:int}/status")]
    public async Task<ActionResult<AdminSupportTicketDetailDto>> UpdateStatus(
        [FromRoute] int ticketId,
        [FromBody] AdminSupportTicketStatusUpdateRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _supportTickets.UpdateStatusAsync(
                GetCurrentUserId(),
                ticketId,
                request,
                cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("{ticketId:int}/in-progress")]
    public async Task<ActionResult<AdminSupportTicketDetailDto>> MarkInProgress(
        [FromRoute] int ticketId,
        [FromBody] AdminSupportTicketStatusUpdateRequestDto? request,
        CancellationToken cancellationToken)
    {
        return await UpdateStatus(
            ticketId,
            new AdminSupportTicketStatusUpdateRequestDto
            {
                Status = SupportRequestStatuses.InProgress,
                InternalNote = request?.InternalNote,
                ResolutionMessage = request?.ResolutionMessage
            },
            cancellationToken);
    }

    [HttpPost("{ticketId:int}/resolve")]
    public async Task<ActionResult<AdminSupportTicketDetailDto>> Resolve(
        [FromRoute] int ticketId,
        [FromBody] AdminSupportTicketStatusUpdateRequestDto? request,
        CancellationToken cancellationToken)
    {
        return await UpdateStatus(
            ticketId,
            new AdminSupportTicketStatusUpdateRequestDto
            {
                Status = SupportRequestStatuses.Resolved,
                InternalNote = request?.InternalNote,
                ResolutionMessage = request?.ResolutionMessage
            },
            cancellationToken);
    }

    [HttpPost("{ticketId:int}/dismiss")]
    public async Task<ActionResult<AdminSupportTicketDetailDto>> Dismiss(
        [FromRoute] int ticketId,
        [FromBody] AdminSupportTicketStatusUpdateRequestDto? request,
        CancellationToken cancellationToken)
    {
        return await UpdateStatus(
            ticketId,
            new AdminSupportTicketStatusUpdateRequestDto
            {
                Status = SupportRequestStatuses.Dismissed,
                InternalNote = request?.InternalNote,
                ResolutionMessage = request?.ResolutionMessage
            },
            cancellationToken);
    }

    [HttpPost("{ticketId:int}/reopen")]
    public async Task<ActionResult<AdminSupportTicketDetailDto>> Reopen(
        [FromRoute] int ticketId,
        [FromBody] AdminSupportTicketStatusUpdateRequestDto? request,
        CancellationToken cancellationToken)
    {
        return await UpdateStatus(
            ticketId,
            new AdminSupportTicketStatusUpdateRequestDto
            {
                Status = SupportRequestStatuses.Open,
                InternalNote = request?.InternalNote,
                ResolutionMessage = request?.ResolutionMessage
            },
            cancellationToken);
    }

    [HttpPost("{ticketId:int}/note")]
    public async Task<ActionResult<AdminSupportTicketDetailDto>> AddNote(
        [FromRoute] int ticketId,
        [FromBody] AdminSupportTicketNoteRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _supportTickets.AddNoteAsync(
                GetCurrentUserId(),
                ticketId,
                request,
                cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    private int GetCurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("userId");

        return int.TryParse(value, out var userId)
            ? userId
            : throw new UnauthorizedAccessException("Invalid admin session.");
    }

    private ActionResult HandleException(Exception ex)
    {
        return ex switch
        {
            UnauthorizedAccessException => StatusCode(StatusCodes.Status403Forbidden, new { message = "Forbidden." }),
            KeyNotFoundException => NotFound(new { message = ex.Message }),
            InvalidOperationException => BadRequest(new { message = ex.Message }),
            _ => HandleUnexpectedException(ex)
        };
    }

    private ActionResult HandleUnexpectedException(Exception ex)
    {
        _logger.LogError(
            ex,
            "Unexpected exception while handling administrator support ticket request {Method} {Path}.",
            HttpContext.Request.Method,
            HttpContext.Request.Path.Value);

        return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An unexpected error occurred." });
    }
}
