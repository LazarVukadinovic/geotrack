using Backend.DataBase;
using Backend.Models;
using Backend.Repo;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services
{
    public interface IAktivnost
    {
        Task<IEnumerable<Aktivnost>> GetAll();
        Task<Aktivnost?> GetById(int id);
        Task<Aktivnost> Create(Aktivnost aktivnost);
        Task<Aktivnost?> Update(int id, Aktivnost aktivnost);
        Task<bool> Delete(int id);
    }

    public class AktivnostServis : IAktivnost
    {
        private readonly IAktivnostRepo aktivnostRepo;
        private readonly IObjekatRepo objekatRepo;
        private readonly IDestinacijaRepo destinacijaRepo;

        public AktivnostServis(IAktivnostRepo aktivnostRepo,IObjekatRepo objekatRepo,IDestinacijaRepo destinacijaRepo)
        {
            this.aktivnostRepo = aktivnostRepo;
            this.objekatRepo = objekatRepo;
            this.destinacijaRepo = destinacijaRepo;
        }

        public async Task<IEnumerable<Aktivnost>> GetAll() => await aktivnostRepo.GetAll();

        public async Task<Aktivnost?> GetById(int id) => await aktivnostRepo.GetById(id);

        public async Task<Aktivnost?> Create(Aktivnost aktivnost)
        {
            
            if (aktivnost.DestinacijaID != null)
            {
                var destinacija = await destinacijaRepo.GetById(aktivnost.DestinacijaID.Value);
                if (destinacija == null) return null;
            }

            
            if (aktivnost.ObjekatID != null)
            {
                var objekat = await objekatRepo.GetById(aktivnost.ObjekatID.Value);
                if (objekat == null) return null;

                if (objekat.DestinacijaID != aktivnost.DestinacijaID)
                    return null;
            }

            return await aktivnostRepo.Create(aktivnost);
        }

        public async Task<Aktivnost?> Update(int id, Aktivnost aktivnost) => await aktivnostRepo.Update(id, aktivnost);

        public async Task<bool> Delete(int id) => await aktivnostRepo.Delete(id);
    }
}
