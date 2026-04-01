using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Application.Interfaces;
using Geotrack.Domain.Entities;

namespace Geotrack.Infrastructure.Repositories
{
    public class UserRepository : Repository<User>,IUserRepository
    {
        public UserRepository(AppDbContext context) : base(context) { }
    }
}
