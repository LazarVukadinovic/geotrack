using Backend.Models;

namespace Backend.Services
{
    public interface IServices
    {
        Task<IEnumerable<Student>> getStudenti();
        Task<Student?> getPoID(int id);

        Task Dodaj(Student s);
        Task Azuriraj(Student s);

        Task Izbrisi(int id);
    }
}
