using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Application.Interfaces.Repository
{
    public interface IRepository<T> where T : class
    {
        public Task<IEnumerable<T>> GetAllAsync();

        public Task<T?> GetByIDAsync(int id);

        public Task AddAsync(T obj);

        public Task Delete(T obj);

        public Task SaveChangesAsync();

    }
}
