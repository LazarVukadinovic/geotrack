using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Domain.Entities
{
    public class PlaceType
    {
        public int Id { get; set; } // PRIMARY KEY
        public string Name { get; set; } = string.Empty;

        public ICollection<Place> Places { get; set;} = new List<Place>(); // NAVIGATIONAL

        public string Description { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool IsActive { get; set; }
    }
}
