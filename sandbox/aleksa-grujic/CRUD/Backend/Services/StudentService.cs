using Backend.Data;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services
{
    public class StudentService : IServices
    {

        private readonly KonekcijaSaBazom kontekst;

        public StudentService(KonekcijaSaBazom konekt)
        {
            kontekst = konekt;
        }

        public async Task<IEnumerable<Student>> getStudenti() => await kontekst.Studenti.ToListAsync();
        

        public async Task Azuriraj(Student s)
        {
            kontekst.Studenti.Update(s);
            await kontekst.SaveChangesAsync();
        }

        public async Task Dodaj(Student s)
        {
            kontekst.Studenti.Add(s);
            await kontekst.SaveChangesAsync();
        }

        public async Task<Student?> getPoID(int id)
        {
            return await kontekst.Studenti.FirstOrDefaultAsync(s => s.Id == id);
        }

        public async Task Izbrisi(int id)
        {
            var student = await getPoID(id);

            if(student != null)
            {
                kontekst.Studenti.Remove(student);

                await kontekst.SaveChangesAsync();
            }
        }
    }

        
}
