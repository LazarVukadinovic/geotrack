using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Domain.Entities.Tags;
using Geotrack.Domain.Entities.Users;

namespace Geotrack.Domain.Entities;
public class UserTag
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
}
