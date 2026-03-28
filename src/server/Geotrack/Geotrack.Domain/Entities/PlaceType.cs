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
    }
}
