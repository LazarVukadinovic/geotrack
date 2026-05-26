using Geotrack.Application.DTOs.Admin;

namespace Geotrack.Application.Interfaces.Service;

public interface IAdminSupportTicketService
{
    Task<AdminSupportTicketListResponseDto> GetTicketsAsync(
        int currentUserId,
        AdminSupportTicketListRequestDto request,
        CancellationToken cancellationToken = default);

    Task<AdminSupportTicketDetailDto> GetTicketAsync(
        int currentUserId,
        int ticketId,
        CancellationToken cancellationToken = default);

    Task<AdminSupportTicketDetailDto> UpdateStatusAsync(
        int currentUserId,
        int ticketId,
        AdminSupportTicketStatusUpdateRequestDto request,
        CancellationToken cancellationToken = default);

    Task<AdminSupportTicketDetailDto> AddNoteAsync(
        int currentUserId,
        int ticketId,
        AdminSupportTicketNoteRequestDto request,
        CancellationToken cancellationToken = default);
}
