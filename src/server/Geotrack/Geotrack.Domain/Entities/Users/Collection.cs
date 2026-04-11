using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Domain.Entities.Users;

public class Collection
{
    public int Id { get; set; } // PRIMARY KEY
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public ICollection<CollectionItem> CollectionItems { get; set; } = new List<CollectionItem>(); // NAVIGATIONAL

    public int UserId { get; set; } // FOREIGN KEY
    public User User { get; set; } = null!; // NAVIGATIONAL
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
