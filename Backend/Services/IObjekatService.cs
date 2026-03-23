using Backend.DataBase;
using Backend.Models;
using Backend.Repo;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services
{
    public interface IObjekatService
    {
        Task<IEnumerable<Objekat>> GetAll();
        Task<Objekat?> GetById(int id);
        Task<Objekat> Create(Objekat objekat);
        Task<Objekat?> Update(int id, Objekat objekat);
        Task<bool> Delete(int id);
    }

    public class ObjekatServis : IObjekatService
    {
        private readonly IObjekatRepo objekatRepo;
        private readonly IDestinacijaRepo destinacijaRepo;

        public ObjekatServis(IObjekatRepo objekatRepo, IDestinacijaRepo destinacijaRepo)
        {
            this.objekatRepo = objekatRepo;
            this.destinacijaRepo = destinacijaRepo;
        }

        public async Task<IEnumerable<Objekat>> GetAll() => await objekatRepo.GetAll();

        public async Task<Objekat?> GetById(int id) => await objekatRepo.GetById(id);

        public async Task<Objekat?> Create(Objekat objekat)
        {
            // Poslovna logika - provjeri da destinacija postoji
            var destinacija = await destinacijaRepo.GetById(objekat.DestinacijaID);
            if (destinacija == null) return null;

            return await objekatRepo.Create(objekat);
        }

        public async Task<Objekat?> Update(int id, Objekat objekat)
        {
            var destinacija = await destinacijaRepo.GetById(objekat.DestinacijaID);
            if (destinacija == null) return null;

            return await objekatRepo.Update(id, objekat);
        }

        public async Task<bool> Delete(int id) => await objekatRepo.Delete(id);
    }

}
