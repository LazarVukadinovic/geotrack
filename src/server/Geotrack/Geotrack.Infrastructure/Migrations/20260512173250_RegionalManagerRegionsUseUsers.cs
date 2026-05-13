using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RegionalManagerRegionsUseUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RegionalManagerRegions_RegionalManagers_UserId",
                table: "RegionalManagerRegions");

            migrationBuilder.DropTable(
                name: "RegionalManagers");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "RegionalManagerRegions",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddForeignKey(
                name: "FK_RegionalManagerRegions_Users_UserId",
                table: "RegionalManagerRegions",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RegionalManagerRegions_Users_UserId",
                table: "RegionalManagerRegions");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "RegionalManagerRegions");

            migrationBuilder.CreateTable(
                name: "RegionalManagers",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegionalManagers", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_RegionalManagers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                INSERT INTO "RegionalManagers" ("UserId")
                SELECT DISTINCT "UserId"
                FROM "RegionalManagerRegions"
                ON CONFLICT ("UserId") DO NOTHING;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_RegionalManagerRegions_RegionalManagers_UserId",
                table: "RegionalManagerRegions",
                column: "UserId",
                principalTable: "RegionalManagers",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
