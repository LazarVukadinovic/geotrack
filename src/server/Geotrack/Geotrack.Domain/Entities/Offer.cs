using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Domain.Entities
{
    public class Offer
    {
        public int Id { get; set; } // PRIMARY KEY

        public string Name { get; set; } = string.Empty;

        public int TravelAgencyId { get; set; } // FOREIGN KEY
        public TravelAgency Agency { get; set; } = null!; // NAVIGATIONAL

        public ICollection<OfferDestination> OfferDestinations { get; set; } = new List<OfferDestination>(); // NAVIGATIONAL
    }
}
