using Backend.DataBase;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Repo
{
    public interface IObjekatRepo
    {
        Task<IEnumerable<Objekat>> GetAll();
        Task<Objekat?> GetById(int id);
        Task<Objekat> Create(Objekat objekat);
        Task<Objekat?> Update(int id, Objekat objekat);
        Task<bool> Delete(int id);
    }

    public class ObjekatRepo : IObjekatRepo
    {

        private readonly AppDbContext context;

        public ObjekatRepo(AppDbContext con)
        { 
            context = con;
        }
        public async Task<Objekat> Create(Objekat objekat)
        {
            context.Objekti.Add(objekat);
            await context.SaveChangesAsync();
            return objekat;
        }

        public async Task<bool> Delete(int id)
        {
            var obj = await context.Objekti.FindAsync(id);
            if (obj == null) return false;

            context.Objekti.Remove(obj);
            await context.SaveChangesAsync();
            return true;
        }

        public async Task<IEnumerable<Objekat>> GetAll()
        {
            return await context.Objekti.Include(o => o.Dogadjaji).Include(o => o.Aktivnosti).ToListAsync();
        }

        public async Task<Objekat?> GetById(int id)
        {
            return await context.Objekti.Include(o => o.Dogadjaji).Include(o => o.Aktivnosti).FirstOrDefaultAsync(o => o.Id == id);
        }

        public async Task<Objekat?> Update(int id, Objekat objekat)
        {
            var obj = await context.Objekti.FindAsync(id);
            if (obj == null) return null;

            obj.ImeObjekta = objekat.ImeObjekta;
            obj.TipObjekta = objekat.TipObjekta;
            obj.Adresa = objekat.Adresa;
            obj.Opis = objekat.Opis;
            obj.kapacitet = objekat.kapacitet;
            obj.OtvaranjeU = objekat.OtvaranjeU;
            obj.ZatvaranjeU = objekat.ZatvaranjeU;
            obj.Xkoordinata = objekat.Xkoordinata;
            obj.YKoordinata = objekat.YKoordinata;
            obj.DestinacijaID = objekat.DestinacijaID;

            await context.SaveChangesAsync();
            return obj;
        }
    }
}
