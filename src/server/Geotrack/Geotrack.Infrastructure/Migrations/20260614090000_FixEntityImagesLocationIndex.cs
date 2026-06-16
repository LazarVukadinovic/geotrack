using Geotrack.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260614090000_FixEntityImagesLocationIndex")]
    public partial class FixEntityImagesLocationIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EntityImages_LocationId",
                table: "EntityImages");

            migrationBuilder.CreateIndex(
                name: "IX_EntityImages_LocationId",
                table: "EntityImages",
                column: "LocationId",
                unique: true,
                filter: "\"LocationId\" IS NOT NULL AND \"IsMain\" = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EntityImages_LocationId",
                table: "EntityImages");

            migrationBuilder.CreateIndex(
                name: "IX_EntityImages_LocationId",
                table: "EntityImages",
                column: "LocationId",
                unique: true);
        }
    }
}
