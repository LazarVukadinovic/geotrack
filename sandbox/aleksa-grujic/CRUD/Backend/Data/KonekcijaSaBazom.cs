using Microsoft.EntityFrameworkCore;
using Backend.Models;

namespace Backend.Data
{
    public class KonekcijaSaBazom : DbContext
    {
        public KonekcijaSaBazom(DbContextOptions<KonekcijaSaBazom> options) : base(options)
        {
        }

        public DbSet<Student> Studenti { get; set; }

    }
}
