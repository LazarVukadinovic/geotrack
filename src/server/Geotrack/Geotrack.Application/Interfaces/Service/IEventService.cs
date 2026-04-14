using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Application.DTOs.Event;

namespace Geotrack.Application.Interfaces.Service
{
    public interface IEventService
    {
        Task<GetEventDetailsResponseDto> GetEventDetailsAsync(int eventId, int? userId);
    }
}
