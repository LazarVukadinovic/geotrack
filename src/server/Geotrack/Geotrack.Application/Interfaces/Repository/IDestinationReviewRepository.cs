using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Domain.Entities.Reviews;

namespace Geotrack.Application.Interfaces.Repository
{
    public interface IDestinationReviewRepository
    {
        Task<bool> ExistsByDestinationAndUserAsync(int destinationId, int userId);
        Task<DestinationReview> CreateAsync(DestinationReview review);
    }
}
