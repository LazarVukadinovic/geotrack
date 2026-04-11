using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Domain.Entities;

public class UserRole
{
    public int UserId { get; set; } // FOREIGN KEY
    public User User { get; set; } = null!; // NAVIGATIONAL

    public int RoleId { get; set; } // FOREIGN KEY
    public Role Role { get; set; } = null!; // NAVIGATIONAL
}