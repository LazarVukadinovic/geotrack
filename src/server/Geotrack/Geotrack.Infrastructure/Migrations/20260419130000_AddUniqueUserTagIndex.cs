using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueUserTagIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Defensively remove any duplicate (UserId, TagId) rows before the
            // unique index is created. Keeps the row with the lowest Id per
            // pair. Safe no-op when no duplicates exist.
            migrationBuilder.Sql(@"
                DELETE FROM ""UserTags"" a
                USING ""UserTags"" b
                WHERE a.""UserId"" = b.""UserId""
                  AND a.""TagId""  = b.""TagId""
                  AND a.""Id""     > b.""Id"";
            ");

            migrationBuilder.CreateIndex(
                name: "IX_UserTags_UserId_TagId",
                table: "UserTags",
                columns: new[] { "UserId", "TagId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserTags_UserId_TagId",
                table: "UserTags");
        }
    }
}
