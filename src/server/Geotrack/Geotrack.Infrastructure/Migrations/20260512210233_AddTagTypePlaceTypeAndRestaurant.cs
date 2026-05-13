using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTagTypePlaceTypeAndRestaurant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tags_PlaceTypes_PlaceTypeId",
                table: "Tags");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserTags",
                table: "UserTags");

            migrationBuilder.DropIndex(
                name: "IX_UserTags_UserId_TagId",
                table: "UserTags");

            migrationBuilder.DropIndex(
                name: "IX_TagTypes_Name",
                table: "TagTypes");

            migrationBuilder.DropIndex(
                name: "IX_Tags_Name",
                table: "Tags");

            migrationBuilder.DropIndex(
                name: "IX_Tags_PlaceTypeId",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "PlaceTypeId",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Stays");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Stays");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "UserTags",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer")
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "TagTypes",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "Tags",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "ImageUrl",
                table: "Tags",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Tags",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000);

            migrationBuilder.AddColumn<int>(
                name: "RestaurantPlaceId",
                table: "Places",
                type: "integer",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserTags",
                table: "UserTags",
                columns: new[] { "UserId", "TagId" });

            migrationBuilder.CreateTable(
                name: "Restaurants",
                columns: table => new
                {
                    PlaceId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Restaurants", x => x.PlaceId);
                    table.ForeignKey(
                        name: "FK_Restaurants_Places_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "Places",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TagTypePlaceTypes",
                columns: table => new
                {
                    TagTypeId = table.Column<int>(type: "integer", nullable: false),
                    PlaceTypeId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagTypePlaceTypes", x => new { x.TagTypeId, x.PlaceTypeId });
                    table.ForeignKey(
                        name: "FK_TagTypePlaceTypes_PlaceTypes_PlaceTypeId",
                        column: x => x.PlaceTypeId,
                        principalTable: "PlaceTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TagTypePlaceTypes_TagTypes_TagTypeId",
                        column: x => x.TagTypeId,
                        principalTable: "TagTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Places_RestaurantPlaceId",
                table: "Places",
                column: "RestaurantPlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_TagTypePlaceTypes_PlaceTypeId",
                table: "TagTypePlaceTypes",
                column: "PlaceTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Places_Restaurants_RestaurantPlaceId",
                table: "Places",
                column: "RestaurantPlaceId",
                principalTable: "Restaurants",
                principalColumn: "PlaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Places_Restaurants_RestaurantPlaceId",
                table: "Places");

            migrationBuilder.DropTable(
                name: "Restaurants");

            migrationBuilder.DropTable(
                name: "TagTypePlaceTypes");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserTags",
                table: "UserTags");

            migrationBuilder.DropIndex(
                name: "IX_Places_RestaurantPlaceId",
                table: "Places");

            migrationBuilder.DropColumn(
                name: "RestaurantPlaceId",
                table: "Places");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "UserTags",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "TagTypes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120);

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "Tags",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120);

            migrationBuilder.AlterColumn<string>(
                name: "ImageUrl",
                table: "Tags",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Tags",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AddColumn<int>(
                name: "PlaceTypeId",
                table: "Tags",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Stays",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Stays",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserTags",
                table: "UserTags",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_UserTags_UserId_TagId",
                table: "UserTags",
                columns: new[] { "UserId", "TagId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TagTypes_Name",
                table: "TagTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Name",
                table: "Tags",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tags_PlaceTypeId",
                table: "Tags",
                column: "PlaceTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tags_PlaceTypes_PlaceTypeId",
                table: "Tags",
                column: "PlaceTypeId",
                principalTable: "PlaceTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
