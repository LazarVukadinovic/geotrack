using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Domain.Entities
{
    public class ActivityType
    {
        public int Id { get; set; } // PRIMARY KEY
        public string Name { get; set; } = string.Empty;
        
        public ICollection<Activity> Activities { get; set; } = new List<Activity>(); // NAVIGATIONAL
    }
}
