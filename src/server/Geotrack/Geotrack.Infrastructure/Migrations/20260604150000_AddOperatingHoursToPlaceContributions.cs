using Geotrack.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260604150000_AddOperatingHoursToPlaceContributions")]
    public partial class AddOperatingHoursToPlaceContributions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "OperatingHoursEnabled",
                table: "PlaceContributions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "OperatingHoursJson",
                table: "PlaceContributions",
                type: "jsonb",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OperatingHoursEnabled",
                table: "PlaceContributions");

            migrationBuilder.DropColumn(
                name: "OperatingHoursJson",
                table: "PlaceContributions");
        }
    }
}
