using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Domain.Entities
{
    public class User
    {
        public int Id { get; set; } // PRIMARY KEY
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>(); // NAVIGATIONAL
    }
}
