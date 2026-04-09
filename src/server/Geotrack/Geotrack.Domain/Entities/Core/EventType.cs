using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Domain.Entities;

public class EventType
{
    public int Id { get; set; } // PRIMARY KEY
    public string Name { get; set; } = string.Empty;

    public ICollection<Event> Events { get; set; } = new List<Event>(); // NAVIGATIONAL

    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
