using System;
using Geotrack.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260527013000_AddRegionBoundaryCandidateMetadata")]
    public partial class AddRegionBoundaryCandidateMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BoundaryAdmLevel",
                table: "Regions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoundaryExternalKey",
                table: "Regions",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BoundaryImportedAt",
                table: "Regions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoundaryLicense",
                table: "Regions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoundaryReleaseType",
                table: "Regions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoundaryShapeGroup",
                table: "Regions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoundaryShapeId",
                table: "Regions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Regions_TerritoryId_BoundaryExternalKey",
                table: "Regions",
                columns: new[] { "TerritoryId", "BoundaryExternalKey" },
                unique: true,
                filter: "\"BoundaryExternalKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Regions_TerritoryId_BoundaryExternalKey",
                table: "Regions");

            migrationBuilder.DropColumn(
                name: "BoundaryAdmLevel",
                table: "Regions");

            migrationBuilder.DropColumn(
                name: "BoundaryExternalKey",
                table: "Regions");

            migrationBuilder.DropColumn(
                name: "BoundaryImportedAt",
                table: "Regions");

            migrationBuilder.DropColumn(
                name: "BoundaryLicense",
                table: "Regions");

            migrationBuilder.DropColumn(
                name: "BoundaryReleaseType",
                table: "Regions");

            migrationBuilder.DropColumn(
                name: "BoundaryShapeGroup",
                table: "Regions");

            migrationBuilder.DropColumn(
                name: "BoundaryShapeId",
                table: "Regions");
        }
    }
}
