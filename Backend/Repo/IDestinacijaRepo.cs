using Backend.DataBase;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Repo
{
    public interface IDestinacijaRepo
    {
        Task<IEnumerable<Destinacija>> GetAll();
        Task<Destinacija?> GetById(int id);
        Task<Destinacija> Create(Destinacija destinacija);
        Task<Destinacija?> Update(int id, Destinacija destinacija);
        Task<bool> Delete(int id);
    }

    public class DestinacijaRepository : IDestinacijaRepo
    {
        private readonly AppDbContext context;

        public DestinacijaRepository(AppDbContext context)
        {
            this.context = context;
        }

        public async Task<IEnumerable<Destinacija>> GetAll()
        {
            return await context.Destinacije.Include(d => d.Objekti).Include(d => d.Aktivnosti).ToListAsync();
        }

        public async Task<Destinacija?> GetById(int id)
        {
            return await context.Destinacije.Include(d => d.Objekti).Include(d => d.Aktivnosti).FirstOrDefaultAsync(d => d.Id == id);
        }

        public async Task<Destinacija> Create(Destinacija destinacija)
        {
            context.Destinacije.Add(destinacija);
            await context.SaveChangesAsync();
            return destinacija;
        }

        public async Task<Destinacija?> Update(int id, Destinacija destinacija)
        {
            var trenutna = await context.Destinacije.FindAsync(id);
            if (trenutna == null) return null;

            trenutna.Ime = destinacija.Ime;
            trenutna.Tip = destinacija.Tip;
            trenutna.Opis = destinacija.Opis;
            trenutna.Slika = destinacija.Slika;
            trenutna.Xkoordinata = destinacija.Xkoordinata;
            trenutna.Ykoordinata = destinacija.Ykoordinata;

            await context.SaveChangesAsync();
            return trenutna;
        }

        public async Task<bool> Delete(int id)
        {
            var destinacija = await context.Destinacije.FindAsync(id);
            if (destinacija == null) return false;

            context.Destinacije.Remove(destinacija);
            await context.SaveChangesAsync();
            return true;
        }
    }
}
