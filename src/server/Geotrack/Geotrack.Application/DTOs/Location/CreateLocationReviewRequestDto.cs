using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Application.DTOs.Location
{
    public class CreateLocationReviewRequestDto
    {
        [Required]
        [Range(1, 5, ErrorMessage = "Rating mora biti između 1 i 5.")]
        public double Rating { get; set; }

        [MaxLength(1000)]
        public string? Comment { get; set; }
    }

    public class CreateLocationReviewResponseDto
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int LocationId { get; set; }
        public double Rating { get; set; }
        public string Comment { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
