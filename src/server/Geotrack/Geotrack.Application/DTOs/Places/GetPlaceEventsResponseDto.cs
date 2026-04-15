using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace Geotrack.Application.DTOs.Places;
public class GetPlaceEventsResponseDto
{
    public int PlaceId { get; set; }
    public List<PlaceEventItemDto> Events { get; set; } = new();
}