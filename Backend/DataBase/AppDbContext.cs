using Microsoft.EntityFrameworkCore;
using Backend.Models;
namespace Backend.DataBase
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Destinacija> Destinacije { get; set; }
        public DbSet<Objekat> Objekti { get; set; }
        public DbSet<Dogadjaj> Dogadjaji { get; set; }
        public DbSet<Aktivnost> Aktivnosti { get; set; }
        public DbSet<Korisnik> Korisnici { get; set; }
    }
}
