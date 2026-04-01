using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Domain.Entities
{
    public class Tag
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;

        public ICollection<DestinationTag> DestinationTags { get; set; } = new List<DestinationTag>();
        public ICollection<PlaceTag> PlaceTags { get; set; } = new List<PlaceTag>();
        public ICollection<EventTag> EventTags { get; set; } = new List<EventTag>();

        public string Description { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
