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

        public string Description { get; set; } = string.Empty;
        public decimal Price { get; set; }

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public int MaxPeople { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool IsActive { get; set; }
    }
}
