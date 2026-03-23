using Backend.DataBase;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Repo
{
    public interface IAktivnostRepo
    {
        Task<IEnumerable<Aktivnost>> GetAll();
        Task<Aktivnost?> GetById(int id);
        Task<Aktivnost> Create(Aktivnost aktivnost);
        Task<Aktivnost?> Update(int id, Aktivnost aktivnost);
        Task<bool> Delete(int id);
    }

    public class AktivnostRepository : IAktivnostRepo
    {
        private readonly AppDbContext context;

        public AktivnostRepository(AppDbContext context)
        {
            this.context = context;
        }

        public async Task<IEnumerable<Aktivnost>> GetAll()
        {
            return await context.Aktivnosti.ToListAsync();
        }

        public async Task<Aktivnost?> GetById(int id)
        {
            return await context.Aktivnosti.FindAsync(id);
        }

        public async Task<Aktivnost> Create(Aktivnost aktivnost)
        {
            context.Aktivnosti.Add(aktivnost);
            await context.SaveChangesAsync();
            return aktivnost;
        }

        public async Task<Aktivnost?> Update(int id, Aktivnost aktivnost)
        {
            var postojeca = await context.Aktivnosti.FindAsync(id);
            if (postojeca == null) return null;

            postojeca.ImeAktivnosti = aktivnost.ImeAktivnosti;
            postojeca.TipAktivnosti = aktivnost.TipAktivnosti;
            postojeca.Opis = aktivnost.Opis;
            postojeca.DestinacijaID = aktivnost.DestinacijaID;
            postojeca.ObjekatID = aktivnost.ObjekatID;
            postojeca.DogadjajID = aktivnost.DogadjajID;

            await context.SaveChangesAsync();
            return postojeca;
        }

        public async Task<bool> Delete(int id)
        {
            var ak = await context.Aktivnosti.FindAsync(id);
            if (ak == null) return false;

            context.Aktivnosti.Remove(ak);
            await context.SaveChangesAsync();
            return true;
        }
    }
}
