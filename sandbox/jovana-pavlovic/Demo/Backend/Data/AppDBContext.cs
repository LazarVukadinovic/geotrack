using Microsoft.EntityFrameworkCore;
using Backend.Models;
namespace Backend.Data
{
    public class AppDBContext : DbContext
    {
        public AppDBContext(DbContextOptions<AppDBContext> options)
        : base(options) { }

        public DbSet<Todo> Todos { get; set; }
    }
}
