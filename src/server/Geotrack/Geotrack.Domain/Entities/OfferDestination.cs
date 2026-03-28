using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Domain.Entities
{
    public class OfferDestination
    {
        public int OfferId { get; set; } // FOREIGN KEY
        public Offer Offer { get; set; } = null!; // NAVIGATIONAL

        public int DestinationId { get; set; } // FOREIGN KEY
        public Destination Destination { get; set; } = null!; // NAVIGATIONAL
    }
}
