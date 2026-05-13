using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTagTypeToTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TagTypeId",
                table: "Tags",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tags_TagTypeId",
                table: "Tags",
                column: "TagTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tags_TagTypes_TagTypeId",
                table: "Tags",
                column: "TagTypeId",
                principalTable: "TagTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tags_TagTypes_TagTypeId",
                table: "Tags");

            migrationBuilder.DropIndex(
                name: "IX_Tags_TagTypeId",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "TagTypeId",
                table: "Tags");
        }
    }
}
