using Geotrack.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260617120000_AddRegionAndTerritoryTagScopes")]
    public partial class AddRegionAndTerritoryTagScopes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ForRegion",
                table: "Tags",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ForTerritory",
                table: "Tags",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ForRegion",
                table: "TagTypes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ForTerritory",
                table: "TagTypes",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ForRegion",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "ForTerritory",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "ForRegion",
                table: "TagTypes");

            migrationBuilder.DropColumn(
                name: "ForTerritory",
                table: "TagTypes");
        }
    }
}
