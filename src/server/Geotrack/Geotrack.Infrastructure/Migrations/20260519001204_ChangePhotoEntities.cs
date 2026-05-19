using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChangePhotoEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageRole",
                table: "PlaceContributionImages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "gallery");

            migrationBuilder.AddColumn<string>(
                name: "ImageRole",
                table: "EntityImages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "gallery");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageRole",
                table: "PlaceContributionImages");

            migrationBuilder.DropColumn(
                name: "ImageRole",
                table: "EntityImages");
        }
    }
}
