using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Application.Interfaces.Repository;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Repositories
{
    public class Repository<T> : IRepository<T> where T : class
    {
        protected readonly AppDbContext context;

        public Repository(AppDbContext context)
        {
            this.context = context;
        }

        public async Task AddAsync(T obj) => await context.Set<T>().AddAsync(obj);

        public Task Delete(T obj)
        {
            context.Set<T>().Remove(obj);
            return Task.CompletedTask;
        }

        public async Task<IEnumerable<T>> GetAllAsync() => await context.Set<T>().ToListAsync();
        public async Task<T?> GetByIDAsync(int id) => await context.Set<T>().FindAsync(id);

        public async Task SaveChangesAsync() => await context.SaveChangesAsync();
    }
}
