using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialModelExpand : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Events_Locations_LocationId",
                table: "Events");

            migrationBuilder.DropForeignKey(
                name: "FK_UserRoles_Roles_RolesId",
                table: "UserRoles");

            migrationBuilder.DropForeignKey(
                name: "FK_UserRoles_Users_UsersId",
                table: "UserRoles");

            migrationBuilder.DropTable(
                name: "LocationActivities");

            migrationBuilder.RenameColumn(
                name: "UsersId",
                table: "UserRoles",
                newName: "RoleId");

            migrationBuilder.RenameColumn(
                name: "RolesId",
                table: "UserRoles",
                newName: "UserId");

            migrationBuilder.RenameIndex(
                name: "IX_UserRoles_UsersId",
                table: "UserRoles",
                newName: "IX_UserRoles_RoleId");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Roles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AddColumn<int>(
                name: "LocationTypeId",
                table: "Locations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EventTypeId",
                table: "Events",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PlaceId",
                table: "Events",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DestinationTypeId",
                table: "Destinations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Activities",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<int>(
                name: "ActivityTypeId",
                table: "Activities",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LocationId",
                table: "Activities",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PlaceId",
                table: "Activities",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ActivityTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DestinationTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DestinationTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EventTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LocationTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlaceTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaceTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TravelAgencies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TravelAgencies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Places",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PlaceTypeId = table.Column<int>(type: "integer", nullable: false),
                    LocationId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Places", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Places_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Places_PlaceTypes_PlaceTypeId",
                        column: x => x.PlaceTypeId,
                        principalTable: "PlaceTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Offers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TravelAgencyId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Offers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Offers_TravelAgencies_TravelAgencyId",
                        column: x => x.TravelAgencyId,
                        principalTable: "TravelAgencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OfferDestinations",
                columns: table => new
                {
                    OfferId = table.Column<int>(type: "integer", nullable: false),
                    DestinationId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfferDestinations", x => new { x.OfferId, x.DestinationId });
                    table.ForeignKey(
                        name: "FK_OfferDestinations_Destinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "Destinations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OfferDestinations_Offers_OfferId",
                        column: x => x.OfferId,
                        principalTable: "Offers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Locations_LocationTypeId",
                table: "Locations",
                column: "LocationTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Events_EventTypeId",
                table: "Events",
                column: "EventTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Events_PlaceId",
                table: "Events",
                column: "PlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Destinations_DestinationTypeId",
                table: "Destinations",
                column: "DestinationTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Activities_ActivityTypeId",
                table: "Activities",
                column: "ActivityTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Activities_LocationId",
                table: "Activities",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Activities_PlaceId",
                table: "Activities",
                column: "PlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityTypes_Name",
                table: "ActivityTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DestinationTypes_Name",
                table: "DestinationTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventTypes_Name",
                table: "EventTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LocationTypes_Name",
                table: "LocationTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OfferDestinations_DestinationId",
                table: "OfferDestinations",
                column: "DestinationId");

            migrationBuilder.CreateIndex(
                name: "IX_OfferDestinations_OfferId",
                table: "OfferDestinations",
                column: "OfferId");

            migrationBuilder.CreateIndex(
                name: "IX_Offers_TravelAgencyId",
                table: "Offers",
                column: "TravelAgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_Places_LocationId",
                table: "Places",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Places_PlaceTypeId",
                table: "Places",
                column: "PlaceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceTypes_Name",
                table: "PlaceTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TravelAgencies_Name",
                table: "TravelAgencies",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Activities_ActivityTypes_ActivityTypeId",
                table: "Activities",
                column: "ActivityTypeId",
                principalTable: "ActivityTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Activities_Locations_LocationId",
                table: "Activities",
                column: "LocationId",
                principalTable: "Locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Activities_Places_PlaceId",
                table: "Activities",
                column: "PlaceId",
                principalTable: "Places",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Destinations_DestinationTypes_DestinationTypeId",
                table: "Destinations",
                column: "DestinationTypeId",
                principalTable: "DestinationTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Events_EventTypes_EventTypeId",
                table: "Events",
                column: "EventTypeId",
                principalTable: "EventTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Events_Locations_LocationId",
                table: "Events",
                column: "LocationId",
                principalTable: "Locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Events_Places_PlaceId",
                table: "Events",
                column: "PlaceId",
                principalTable: "Places",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Locations_LocationTypes_LocationTypeId",
                table: "Locations",
                column: "LocationTypeId",
                principalTable: "LocationTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserRoles_Roles_RoleId",
                table: "UserRoles",
                column: "RoleId",
                principalTable: "Roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserRoles_Users_UserId",
                table: "UserRoles",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Activities_ActivityTypes_ActivityTypeId",
                table: "Activities");

            migrationBuilder.DropForeignKey(
                name: "FK_Activities_Locations_LocationId",
                table: "Activities");

            migrationBuilder.DropForeignKey(
                name: "FK_Activities_Places_PlaceId",
                table: "Activities");

            migrationBuilder.DropForeignKey(
                name: "FK_Destinations_DestinationTypes_DestinationTypeId",
                table: "Destinations");

            migrationBuilder.DropForeignKey(
                name: "FK_Events_EventTypes_EventTypeId",
                table: "Events");

            migrationBuilder.DropForeignKey(
                name: "FK_Events_Locations_LocationId",
                table: "Events");

            migrationBuilder.DropForeignKey(
                name: "FK_Events_Places_PlaceId",
                table: "Events");

            migrationBuilder.DropForeignKey(
                name: "FK_Locations_LocationTypes_LocationTypeId",
                table: "Locations");

            migrationBuilder.DropForeignKey(
                name: "FK_UserRoles_Roles_RoleId",
                table: "UserRoles");

            migrationBuilder.DropForeignKey(
                name: "FK_UserRoles_Users_UserId",
                table: "UserRoles");

            migrationBuilder.DropTable(
                name: "ActivityTypes");

            migrationBuilder.DropTable(
                name: "DestinationTypes");

            migrationBuilder.DropTable(
                name: "EventTypes");

            migrationBuilder.DropTable(
                name: "LocationTypes");

            migrationBuilder.DropTable(
                name: "OfferDestinations");

            migrationBuilder.DropTable(
                name: "Places");

            migrationBuilder.DropTable(
                name: "Offers");

            migrationBuilder.DropTable(
                name: "PlaceTypes");

            migrationBuilder.DropTable(
                name: "TravelAgencies");

            migrationBuilder.DropIndex(
                name: "IX_Users_Username",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Locations_LocationTypeId",
                table: "Locations");

            migrationBuilder.DropIndex(
                name: "IX_Events_EventTypeId",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Events_PlaceId",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Destinations_DestinationTypeId",
                table: "Destinations");

            migrationBuilder.DropIndex(
                name: "IX_Activities_ActivityTypeId",
                table: "Activities");

            migrationBuilder.DropIndex(
                name: "IX_Activities_LocationId",
                table: "Activities");

            migrationBuilder.DropIndex(
                name: "IX_Activities_PlaceId",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "LocationTypeId",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "EventTypeId",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "PlaceId",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "DestinationTypeId",
                table: "Destinations");

            migrationBuilder.DropColumn(
                name: "ActivityTypeId",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "PlaceId",
                table: "Activities");

            migrationBuilder.RenameColumn(
                name: "RoleId",
                table: "UserRoles",
                newName: "UsersId");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "UserRoles",
                newName: "RolesId");

            migrationBuilder.RenameIndex(
                name: "IX_UserRoles_RoleId",
                table: "UserRoles",
                newName: "IX_UserRoles_UsersId");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Roles",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Activities",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.CreateTable(
                name: "LocationActivities",
                columns: table => new
                {
                    ActivitiesId = table.Column<int>(type: "integer", nullable: false),
                    LocationsId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationActivities", x => new { x.ActivitiesId, x.LocationsId });
                    table.ForeignKey(
                        name: "FK_LocationActivities_Activities_ActivitiesId",
                        column: x => x.ActivitiesId,
                        principalTable: "Activities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LocationActivities_Locations_LocationsId",
                        column: x => x.LocationsId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LocationActivities_LocationsId",
                table: "LocationActivities",
                column: "LocationsId");

            migrationBuilder.AddForeignKey(
                name: "FK_Events_Locations_LocationId",
                table: "Events",
                column: "LocationId",
                principalTable: "Locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserRoles_Roles_RolesId",
                table: "UserRoles",
                column: "RolesId",
                principalTable: "Roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserRoles_Users_UsersId",
                table: "UserRoles",
                column: "UsersId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
