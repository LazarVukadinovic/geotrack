using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.ComponentModel.DataAnnotations;
namespace Geotrack.Application.DTOs.Locations;
public class CreateLocationReviewRequestDto
{
    [Range(1, 5)]
    public double Rating { get; set; }
    [MaxLength(1000)]
    public string Comment { get; set; } = string.Empty;
}