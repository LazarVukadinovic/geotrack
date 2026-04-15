using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace Geotrack.Application.DTOs.Places;
public class PlaceReviewItemDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public double Rating { get; set; }
    public string Comment { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
