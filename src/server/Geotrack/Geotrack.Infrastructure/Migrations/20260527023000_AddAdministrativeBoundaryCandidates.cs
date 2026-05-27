using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260527023000_AddAdministrativeBoundaryCandidates")]
    public partial class AddAdministrativeBoundaryCandidates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdministrativeBoundaryCandidates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReleaseType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AdmLevel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ShapeGroup = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ShapeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ShapeName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ShapeType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ExternalKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CountryIso3 = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CountryTerritoryId = table.Column<int>(type: "integer", nullable: true),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BoundaryGeometry = table.Column<NetTopologySuite.Geometries.Geometry>(type: "geometry (geometry, 4326)", nullable: true),
                    BoundaryDisplayGeometry = table.Column<NetTopologySuite.Geometries.Geometry>(type: "geometry (geometry, 4326)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdministrativeBoundaryCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdministrativeBoundaryCandidates_Territories_CountryTerritoryId",
                        column: x => x.CountryTerritoryId,
                        principalTable: "Territories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdministrativeBoundaryCandidates_BoundaryGeometry",
                table: "AdministrativeBoundaryCandidates",
                column: "BoundaryGeometry")
                .Annotation("Npgsql:IndexMethod", "GIST");

            migrationBuilder.CreateIndex(
                name: "IX_AdministrativeBoundaryCandidates_CountryIso3_AdmLevel",
                table: "AdministrativeBoundaryCandidates",
                columns: new[] { "CountryIso3", "AdmLevel" });

            migrationBuilder.CreateIndex(
                name: "IX_AdministrativeBoundaryCandidates_CountryTerritoryId",
                table: "AdministrativeBoundaryCandidates",
                column: "CountryTerritoryId");

            migrationBuilder.CreateIndex(
                name: "IX_AdministrativeBoundaryCandidates_ExternalKey",
                table: "AdministrativeBoundaryCandidates",
                column: "ExternalKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdministrativeBoundaryCandidates_Source_AdmLevel_ShapeGroup_ShapeId",
                table: "AdministrativeBoundaryCandidates",
                columns: new[] { "Source", "AdmLevel", "ShapeGroup", "ShapeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdministrativeBoundaryCandidates");
        }
    }
}
