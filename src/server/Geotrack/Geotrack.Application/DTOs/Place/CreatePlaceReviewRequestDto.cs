using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Application.DTOs.Place
{
    public class CreatePlaceReviewRequestDto
    {
        public double Rating { get; set; }
        public string Comment { get; set; } = string.Empty;
    }
}
