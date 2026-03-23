
using Backend.DataBase;
using Backend.Models;
using Backend.Repo;
using Microsoft.EntityFrameworkCore;
namespace Backend.Services
{
    public interface IDestinacijaService
    {
        Task<IEnumerable<Destinacija>> GetAll();
        Task<Destinacija?> GetById(int id);
        Task<Destinacija> Create(Destinacija destinacija);
        Task<Destinacija?> Update(int id,Destinacija destinacija);

        Task<bool>  Delete(int id);

    }


    public class DestinacijaServis : IDestinacijaService
    {
        private readonly IDestinacijaRepo repository;

        public DestinacijaServis(IDestinacijaRepo repository)
        {
            this.repository = repository;
        }

        public async Task<IEnumerable<Destinacija>> GetAll() => await repository.GetAll();

        public async Task<Destinacija?> GetById(int id) => await repository.GetById(id);

        public async Task<Destinacija> Create(Destinacija destinacija)
        {
            var sve = await repository.GetAll();
            bool postoji = sve.Any(d => d.Ime.ToLower() == destinacija.Ime.ToLower());
            
            return await repository.Create(destinacija);
        }

        public async Task<Destinacija?> Update(int id, Destinacija destinacija) => await repository.Update(id, destinacija);

        public async Task<bool> Delete(int id) => await repository.Delete(id);
    }
}
