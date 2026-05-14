using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFilterFieldsToTagTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FilterLabel",
                table: "TagTypes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FilterOrder",
                table: "TagTypes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsFilterable",
                table: "TagTypes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_TagTypes_IsFilterable_FilterOrder",
                table: "TagTypes",
                columns: new[] { "IsFilterable", "FilterOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TagTypes_IsFilterable_FilterOrder",
                table: "TagTypes");

            migrationBuilder.DropColumn(
                name: "FilterLabel",
                table: "TagTypes");

            migrationBuilder.DropColumn(
                name: "FilterOrder",
                table: "TagTypes");

            migrationBuilder.DropColumn(
                name: "IsFilterable",
                table: "TagTypes");
        }
    }
}
