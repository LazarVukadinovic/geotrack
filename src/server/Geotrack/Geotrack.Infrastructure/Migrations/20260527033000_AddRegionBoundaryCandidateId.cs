using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260527033000_AddRegionBoundaryCandidateId")]
    public partial class AddRegionBoundaryCandidateId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BoundaryCandidateId",
                table: "Regions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Regions_BoundaryCandidateId",
                table: "Regions",
                column: "BoundaryCandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_Regions_TerritoryId_BoundaryCandidateId",
                table: "Regions",
                columns: new[] { "TerritoryId", "BoundaryCandidateId" },
                unique: true,
                filter: "\"BoundaryCandidateId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Regions_AdministrativeBoundaryCandidates_BoundaryCandidateId",
                table: "Regions",
                column: "BoundaryCandidateId",
                principalTable: "AdministrativeBoundaryCandidates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Regions_AdministrativeBoundaryCandidates_BoundaryCandidateId",
                table: "Regions");

            migrationBuilder.DropIndex(
                name: "IX_Regions_BoundaryCandidateId",
                table: "Regions");

            migrationBuilder.DropIndex(
                name: "IX_Regions_TerritoryId_BoundaryCandidateId",
                table: "Regions");

            migrationBuilder.DropColumn(
                name: "BoundaryCandidateId",
                table: "Regions");
        }
    }
}
