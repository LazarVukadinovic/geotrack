using Geotrack.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);


// Add services to the container.

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


// builder.Services.AddScoped<IDestinationRepository, DestinationRepository>();
// builder.Services.AddScoped<IUserRepository, UserRepository>();
// builder.Services.AddScoped<IRoleRepository, RoleRepository>();

// builder.Services.AddScoped<UserService>();
// builder.Services.AddScoped<RoleService>();
// builder.Services.AddScoped<DestinationService>();
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
