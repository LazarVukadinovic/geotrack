using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Application.DTOs.Place;

namespace Geotrack.Application.Interfaces.Service
{
    public interface IPlaceService
    {
        Task<GetPlaceDetailsResponseDto> GetPlaceDetailsAsync(int placeId, int? userId);
        public Task<GetPlaceEventsResponseDto> GetPlaceEventsAsync(int placeId);
    }
}
