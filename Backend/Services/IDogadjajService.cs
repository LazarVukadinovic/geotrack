using Backend.DataBase;
using Backend.Models;
using Backend.Repo;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services
{
    public interface IDogadjajService
    {
        Task<IEnumerable<Dogadjaj>> GetAll();
        Task<Dogadjaj?> GetById(int id);
        Task<Dogadjaj> Create(Dogadjaj dogadjaj);
        Task<Dogadjaj?> Update(int id, Dogadjaj dogadjaj);
        Task<bool> Delete(int id);
    }

    public class DogadjajServis : IDogadjajService
    {
        private readonly IDogadjajRepo dogadjajRepo;
        private readonly IObjekatRepo objekatRepo;

        public DogadjajServis(IDogadjajRepo dogadjajRepo, IObjekatRepo objekatRepo)
        {
            this.dogadjajRepo = dogadjajRepo;
            this.objekatRepo = objekatRepo;
        }

        public async Task<IEnumerable<Dogadjaj>> GetAll() => await dogadjajRepo.GetAll();

        public async Task<Dogadjaj?> GetById(int id) => await dogadjajRepo.GetById(id);

        public async Task<Dogadjaj?> Create(Dogadjaj dogadjaj)
        {
            
            var objekat = await objekatRepo.GetById(dogadjaj.ObjekatID);
            if (objekat == null) return null;

            
            if (objekat.kapacitet != null && dogadjaj.maxBrojLjudi > objekat.kapacitet)
                return null;

            var postojeci = await dogadjajRepo.GetAll();
            bool preklapanje = postojeci.Any(d =>
                d.ObjekatID == dogadjaj.ObjekatID &&
                d.DatumPocetka < dogadjaj.DatumZavrsetka &&
                d.DatumZavrsetka > dogadjaj.DatumPocetka);

            if (preklapanje) return null;

            return await dogadjajRepo.Create(dogadjaj);
        }

        public async Task<Dogadjaj?> Update(int id, Dogadjaj dogadjaj)
        {
            // Provjeri da objekat postoji
            var objekat = await objekatRepo.GetById(dogadjaj.ObjekatID);
            if (objekat == null) return null;

            return await dogadjajRepo.Update(id, dogadjaj);
        }

        public async Task<bool> Delete(int id) => await dogadjajRepo.Delete(id);
    }
}
