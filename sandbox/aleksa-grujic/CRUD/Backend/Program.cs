
using Backend.Data;
using Microsoft.EntityFrameworkCore;
using Backend.Controllers;
using Backend.Services;
using Backend.Models;
using Npgsql.EntityFrameworkCore.PostgreSQL;

namespace Backend
{
    public class Program
    {
        public static void Main(string[] args)
        {

            var builder = WebApplication.CreateBuilder(args);

            var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

            builder.Services.AddDbContext<KonekcijaSaBazom>(options =>options.UseNpgsql( builder.Configuration.GetConnectionString("DefaultConnection")));

            // Add services to the container.
            builder.Services.AddControllers();

            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            builder.Services.AddScoped<IServices, StudentService>();

            builder.Services.AddCors(options => { options.AddPolicy("Angular", policy => { policy.WithOrigins("http://localhost:4200").AllowAnyHeader().AllowAnyMethod();});});

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.UseAuthorization();

            app.UseCors("Angular");

            app.MapControllers();

            app.Run();
        }
    }
}
