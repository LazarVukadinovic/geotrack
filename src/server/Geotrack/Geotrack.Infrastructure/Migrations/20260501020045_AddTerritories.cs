using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTerritories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Territories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Territories", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Territories",
                columns: new[] { "Id", "Name", "CreatedAt", "UpdatedAt" },
                values: new object[] { 1, "Default", new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), null });

            migrationBuilder.Sql(
                "SELECT setval(pg_get_serial_sequence('\"Territories\"', 'Id'), (SELECT MAX(\"Id\") FROM \"Territories\"));");

            migrationBuilder.DropIndex(
                name: "IX_Regions_Name",
                table: "Regions");

            migrationBuilder.AddColumn<int>(
                name: "TerritoryId",
                table: "Regions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_Regions_TerritoryId",
                table: "Regions",
                column: "TerritoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Regions_TerritoryId_Name",
                table: "Regions",
                columns: new[] { "TerritoryId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Territories_Name",
                table: "Territories",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Regions_Territories_TerritoryId",
                table: "Regions",
                column: "TerritoryId",
                principalTable: "Territories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Regions_Territories_TerritoryId",
                table: "Regions");

            migrationBuilder.DropTable(
                name: "Territories");

            migrationBuilder.DropIndex(
                name: "IX_Regions_TerritoryId",
                table: "Regions");

            migrationBuilder.DropIndex(
                name: "IX_Regions_TerritoryId_Name",
                table: "Regions");

            migrationBuilder.DropColumn(
                name: "TerritoryId",
                table: "Regions");

            migrationBuilder.CreateIndex(
                name: "IX_Regions_Name",
                table: "Regions",
                column: "Name",
                unique: true);
        }
    }
}
