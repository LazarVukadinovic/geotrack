using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Application.DTOs.Destionation;

namespace Geotrack.Application.Interfaces.Service
{
    public interface IDestinationReviewService
    {
        Task<CreateDestinationReviewResponseDto> CreateReviewAsync(int destinationId, int userId, CreateDestinationReviewRequestDto dto);
    }
}
