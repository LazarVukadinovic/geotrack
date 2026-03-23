using Backend.DataBase;
using Backend.Models;
using Microsoft.EntityFrameworkCore;
namespace Backend.Repo
{
    public interface IDogadjajRepo
    {
        Task<IEnumerable<Dogadjaj>> GetAll();
        Task<Dogadjaj?> GetById(int id);
        Task<Dogadjaj> Create(Dogadjaj dogadjaj);
        Task<Dogadjaj?> Update(int id, Dogadjaj dogadjaj);
        Task<bool> Delete(int id);
    }

    public class DogadjajRepository : IDogadjajRepo
    {
        private readonly AppDbContext context;

        public DogadjajRepository(AppDbContext context)
        {
            this.context = context;
        }

        public async Task<IEnumerable<Dogadjaj>> GetAll()
        {
            return await context.Dogadjaji.Include(d => d.Prijavljeni).Include(d => d.AktivnostiNaDogadjaju).ToListAsync();
        }

        public async Task<Dogadjaj?> GetById(int id)
        {
            return await context.Dogadjaji.Include(d => d.Prijavljeni).Include(d => d.AktivnostiNaDogadjaju).FirstOrDefaultAsync(d => d.Id == id);
        }

        public async Task<Dogadjaj> Create(Dogadjaj dogadjaj)
        {
            context.Dogadjaji.Add(dogadjaj);
            await context.SaveChangesAsync();
            return dogadjaj;
        }

        public async Task<Dogadjaj?> Update(int id, Dogadjaj dogadjaj)
        {
            var trenutni = await context.Dogadjaji.FindAsync(id);
            if (trenutni == null) return null;

            trenutni.ImeDogadjaja = dogadjaj.ImeDogadjaja;
            trenutni.TipDogadjaja = dogadjaj.TipDogadjaja;
            trenutni.Opis = dogadjaj.Opis;
            trenutni.maxBrojLjudi = dogadjaj.maxBrojLjudi;
            trenutni.DatumPocetka = dogadjaj.DatumPocetka;
            trenutni.DatumZavrsetka = dogadjaj.DatumZavrsetka;
            trenutni.ObjekatID = dogadjaj.ObjekatID;

            await context.SaveChangesAsync();
            return trenutni;
        }

        public async Task<bool> Delete(int id)
        {
            var dog = await context.Dogadjaji.FindAsync(id);
            if (dog == null) return false;

            context.Dogadjaji.Remove(dog);
            await context.SaveChangesAsync();
            return true;
        }
    }
}
