using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnershipProofToPlaceContributions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OwnershipProofContentType",
                table: "PlaceContributions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnershipProofDocumentUrl",
                table: "PlaceContributions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnershipProofOriginalFileName",
                table: "PlaceContributions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OwnershipProofUploadedAt",
                table: "PlaceContributions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OwnershipProofContentType",
                table: "PlaceContributions");

            migrationBuilder.DropColumn(
                name: "OwnershipProofDocumentUrl",
                table: "PlaceContributions");

            migrationBuilder.DropColumn(
                name: "OwnershipProofOriginalFileName",
                table: "PlaceContributions");

            migrationBuilder.DropColumn(
                name: "OwnershipProofUploadedAt",
                table: "PlaceContributions");
        }
    }
}
