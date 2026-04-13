using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Application.DTOs.Destionation
{
    public class CreateDestinationReviewRequestDto
    {
        [Range(1, 5)]
        public double Rating { get; set; }

        [Required]
        [MaxLength(1000)]
        public string Comment { get; set; } 

    }

    public class CreateDestinationReviewResponseDto
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int DestinationId { get; set; }
        public double Rating { get; set; }
        public string Comment { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }


}
