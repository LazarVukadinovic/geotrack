
using Backend.DataBase;
using Backend.Repo;
using Backend.Services;
using Microsoft.EntityFrameworkCore;

namespace Backend
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle

            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

            // Repo
            builder.Services.AddScoped<IDestinacijaRepo, DestinacijaRepository>();
            builder.Services.AddScoped<IObjekatRepo, ObjekatRepo>();
            builder.Services.AddScoped<IDogadjajRepo, DogadjajRepository>();
            builder.Services.AddScoped<IAktivnostRepo, AktivnostRepository>();

            // Services
            builder.Services.AddScoped<IDestinacijaService, DestinacijaServis>();
            builder.Services.AddScoped<IObjekatService, ObjekatServis>();
            builder.Services.AddScoped<IDogadjajService, DogadjajServis>();
            builder.Services.AddScoped<IAktivnost, AktivnostServis>();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }
    }
}
