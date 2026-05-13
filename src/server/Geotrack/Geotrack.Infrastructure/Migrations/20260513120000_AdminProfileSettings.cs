using Geotrack.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260513120000_AdminProfileSettings")]
    public partial class AdminProfileSettings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EmailNewReviewEnabled",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmailRequestsEnabled",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "InterfaceLanguage",
                table: "Users",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "en-US");

            migrationBuilder.AddColumn<string>(
                name: "ShortBio",
                table: "Users",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Timezone",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "UTC");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmailNewReviewEnabled",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EmailRequestsEnabled",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "InterfaceLanguage",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ShortBio",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Timezone",
                table: "Users");
        }
    }
}
