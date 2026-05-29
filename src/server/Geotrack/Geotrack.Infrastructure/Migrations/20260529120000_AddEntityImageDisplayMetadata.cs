using Geotrack.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260529120000_AddEntityImageDisplayMetadata")]
    public partial class AddEntityImageDisplayMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImagePosition",
                table: "EntityImages",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "50% 50%");

            migrationBuilder.AddColumn<string>(
                name: "ImageSize",
                table: "EntityImages",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "cover");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImagePosition",
                table: "EntityImages");

            migrationBuilder.DropColumn(
                name: "ImageSize",
                table: "EntityImages");
        }
    }
}
