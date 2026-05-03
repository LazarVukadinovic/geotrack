using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateTable(
                name: "Amenities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Icon = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Amenities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AnonymousSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EntryRoute = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CoarseRegion = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CoarseCountry = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    UserAgentFamily = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false, defaultValue: "other")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnonymousSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BookingPartners",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Variant = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingPartners", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DestinationTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
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
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
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
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
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
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaceTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StayAmenities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IconKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StayAmenities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StayTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StayTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TagTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ForDestination = table.Column<bool>(type: "boolean", nullable: false),
                    ForLocation = table.Column<bool>(type: "boolean", nullable: false),
                    ForPlace = table.Column<bool>(type: "boolean", nullable: false),
                    ForEvent = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Territories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Territories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AvatarUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    NeedsInterestsOnboarding = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    GeneralNotificationsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    TravelRecommendationsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    NearbyEventsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CollectionUpdatesEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    EmailNotificationsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    AnalyticsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ForDestination = table.Column<bool>(type: "boolean", nullable: false),
                    ForLocation = table.Column<bool>(type: "boolean", nullable: false),
                    ForPlace = table.Column<bool>(type: "boolean", nullable: false),
                    ForEvent = table.Column<bool>(type: "boolean", nullable: false),
                    PlaceTypeId = table.Column<int>(type: "integer", nullable: true),
                    ImageUrl = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tags_PlaceTypes_PlaceTypeId",
                        column: x => x.PlaceTypeId,
                        principalTable: "PlaceTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Regions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TerritoryId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Regions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Regions_Territories_TerritoryId",
                        column: x => x.TerritoryId,
                        principalTable: "Territories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Collections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Collections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Collections_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Favorites",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Favorites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Favorites_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ModerationTerms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Term = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    NormalizedTerm = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    LanguageCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    MatchType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModerationTerms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModerationTerms_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ModerationTerms_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsPersistent = table.Column<bool>(type: "boolean", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsRevoked = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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

            migrationBuilder.CreateTable(
                name: "SupportRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Subject = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportRequests_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TerritoryManagers",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TerritoryManagers", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_TerritoryManagers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserNotifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    RelatedEntityType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    RelatedEntityId = table.Column<int>(type: "integer", nullable: true),
                    ActionUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserNotifications_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    RoleId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_UserRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRoles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserInterestSignals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    TagId = table.Column<int>(type: "integer", nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "inferred"),
                    LastInteractionAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastReason = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    IsDismissed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DismissedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DismissedAtScore = table.Column<double>(type: "double precision", nullable: true),
                    EvidenceSinceDismissal = table.Column<double>(type: "double precision", nullable: false, defaultValue: 0.0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserInterestSignals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserInterestSignals_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserInterestSignals_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserTags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    TagId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserTags_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Destinations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DestinationTypeId = table.Column<int>(type: "integer", nullable: false),
                    RegionId = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Coordinates = table.Column<Point>(type: "geography (point)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Destinations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Destinations_DestinationTypes_DestinationTypeId",
                        column: x => x.DestinationTypeId,
                        principalTable: "DestinationTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Destinations_Regions_RegionId",
                        column: x => x.RegionId,
                        principalTable: "Regions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RegionalManagerRegions",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    RegionId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegionalManagerRegions", x => new { x.UserId, x.RegionId });
                    table.ForeignKey(
                        name: "FK_RegionalManagerRegions_RegionalManagers_UserId",
                        column: x => x.UserId,
                        principalTable: "RegionalManagers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RegionalManagerRegions_Regions_RegionId",
                        column: x => x.RegionId,
                        principalTable: "Regions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TerritoryManagerTerritories",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    TerritoryId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TerritoryManagerTerritories", x => new { x.UserId, x.TerritoryId });
                    table.ForeignKey(
                        name: "FK_TerritoryManagerTerritories_Territories_TerritoryId",
                        column: x => x.TerritoryId,
                        principalTable: "Territories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TerritoryManagerTerritories_TerritoryManagers_UserId",
                        column: x => x.UserId,
                        principalTable: "TerritoryManagers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AnalyticsEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    AnonymousSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    EntityId = table.Column<int>(type: "integer", nullable: true),
                    DestinationId = table.Column<int>(type: "integer", nullable: true),
                    Route = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClientOccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalyticsEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnalyticsEvents_AnonymousSessions_AnonymousSessionId",
                        column: x => x.AnonymousSessionId,
                        principalTable: "AnonymousSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AnalyticsEvents_Destinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "Destinations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AnalyticsEvents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "DestinationReviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    DestinationId = table.Column<int>(type: "integer", nullable: false),
                    Rating = table.Column<double>(type: "double precision", nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DestinationReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DestinationReviews_Destinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "Destinations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DestinationReviews_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DestinationTags",
                columns: table => new
                {
                    DestinationId = table.Column<int>(type: "integer", nullable: false),
                    TagId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DestinationTags", x => new { x.DestinationId, x.TagId });
                    table.ForeignKey(
                        name: "FK_DestinationTags_Destinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "Destinations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DestinationTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Locations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DestinationId = table.Column<int>(type: "integer", nullable: false),
                    LocationTypeId = table.Column<int>(type: "integer", nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Coordinates = table.Column<Point>(type: "geography (point)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Locations_Destinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "Destinations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Locations_LocationTypes_LocationTypeId",
                        column: x => x.LocationTypeId,
                        principalTable: "LocationTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LocationReviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    LocationId = table.Column<int>(type: "integer", nullable: false),
                    Rating = table.Column<double>(type: "double precision", nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LocationReviews_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LocationReviews_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LocationTags",
                columns: table => new
                {
                    LocationId = table.Column<int>(type: "integer", nullable: false),
                    TagId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationTags", x => new { x.LocationId, x.TagId });
                    table.ForeignKey(
                        name: "FK_LocationTags_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LocationTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Places",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Subtitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OpeningHours = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AverageRating = table.Column<double>(type: "double precision", nullable: true),
                    ReviewCount = table.Column<int>(type: "integer", nullable: false),
                    PlaceTypeId = table.Column<int>(type: "integer", nullable: false),
                    LocationId = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    ContactEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Coordinates = table.Column<Point>(type: "geography (point)", nullable: true)
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
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EventTypeId = table.Column<int>(type: "integer", nullable: false),
                    LocationId = table.Column<int>(type: "integer", nullable: false),
                    PlaceId = table.Column<int>(type: "integer", nullable: true),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TicketPrice = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Duration = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    HostedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    VenueDescription = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ArrivalTips = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Accessibility = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Coordinates = table.Column<Point>(type: "geography (point)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Events_EventTypes_EventTypeId",
                        column: x => x.EventTypeId,
                        principalTable: "EventTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Events_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Events_Places_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "Places",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PlaceAmenities",
                columns: table => new
                {
                    PlaceId = table.Column<int>(type: "integer", nullable: false),
                    AmenityId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaceAmenities", x => new { x.PlaceId, x.AmenityId });
                    table.ForeignKey(
                        name: "FK_PlaceAmenities_Amenities_AmenityId",
                        column: x => x.AmenityId,
                        principalTable: "Amenities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlaceAmenities_Places_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "Places",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlaceBookingPartners",
                columns: table => new
                {
                    PlaceId = table.Column<int>(type: "integer", nullable: false),
                    BookingPartnerId = table.Column<int>(type: "integer", nullable: false),
                    Url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaceBookingPartners", x => new { x.PlaceId, x.BookingPartnerId });
                    table.ForeignKey(
                        name: "FK_PlaceBookingPartners_BookingPartners_BookingPartnerId",
                        column: x => x.BookingPartnerId,
                        principalTable: "BookingPartners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlaceBookingPartners_Places_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "Places",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlaceContributions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    PlaceId = table.Column<int>(type: "integer", nullable: true),
                    ReviewedByUserId = table.Column<int>(type: "integer", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    OriginalSnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    PlaceTypeId = table.Column<int>(type: "integer", nullable: true),
                    LocationId = table.Column<int>(type: "integer", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Subtitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OpeningHours = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    ContactEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PhoneNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Website = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RestaurantTypeId = table.Column<int>(type: "integer", nullable: true),
                    StayTypeId = table.Column<int>(type: "integer", nullable: true),
                    PricePerNight = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    CheckInTime = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CheckOutTime = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    TagsJson = table.Column<string>(type: "jsonb", nullable: false),
                    AmenitiesJson = table.Column<string>(type: "jsonb", nullable: false),
                    StayAmenitiesJson = table.Column<string>(type: "jsonb", nullable: false),
                    BookingPartnersJson = table.Column<string>(type: "jsonb", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaceContributions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaceContributions_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PlaceContributions_PlaceTypes_PlaceTypeId",
                        column: x => x.PlaceTypeId,
                        principalTable: "PlaceTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PlaceContributions_Places_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "Places",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PlaceContributions_RestaurantTypes_RestaurantTypeId",
                        column: x => x.RestaurantTypeId,
                        principalTable: "RestaurantTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PlaceContributions_StayTypes_StayTypeId",
                        column: x => x.StayTypeId,
                        principalTable: "StayTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PlaceContributions_Users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PlaceContributions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlaceReviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    PlaceId = table.Column<int>(type: "integer", nullable: false),
                    Rating = table.Column<double>(type: "double precision", nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaceReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaceReviews_Places_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "Places",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlaceReviews_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlaceTags",
                columns: table => new
                {
                    PlaceId = table.Column<int>(type: "integer", nullable: false),
                    TagId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaceTags", x => new { x.PlaceId, x.TagId });
                    table.ForeignKey(
                        name: "FK_PlaceTags_Places_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "Places",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlaceTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Stays",
                columns: table => new
                {
                    PlaceId = table.Column<int>(type: "integer", nullable: false),
                    StayTypeId = table.Column<int>(type: "integer", nullable: false),
                    PricePerNight = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    CheckInTime = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CheckOutTime = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stays", x => x.PlaceId);
                    table.ForeignKey(
                        name: "FK_Stays_Places_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "Places",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Stays_StayTypes_StayTypeId",
                        column: x => x.StayTypeId,
                        principalTable: "StayTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserPlaces",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    PlaceId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPlaces", x => new { x.UserId, x.PlaceId });
                    table.ForeignKey(
                        name: "FK_UserPlaces_Places_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "Places",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserPlaces_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CollectionItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CollectionId = table.Column<int>(type: "integer", nullable: false),
                    DestinationId = table.Column<int>(type: "integer", nullable: true),
                    LocationId = table.Column<int>(type: "integer", nullable: true),
                    EventId = table.Column<int>(type: "integer", nullable: true),
                    PlaceId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CollectionItems_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CollectionItems_Destinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "Destinations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CollectionItems_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CollectionItems_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CollectionItems_Places_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "Places",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "EntityImages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TargetType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PlaceId = table.Column<int>(type: "integer", nullable: true),
                    EventId = table.Column<int>(type: "integer", nullable: true),
                    RegionId = table.Column<int>(type: "integer", nullable: true),
                    DestinationId = table.Column<int>(type: "integer", nullable: true),
                    TerritoryId = table.Column<int>(type: "integer", nullable: true),
                    LocationId = table.Column<int>(type: "integer", nullable: true),
                    Url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    AltText = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Caption = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsMain = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntityImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EntityImages_Destinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "Destinations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EntityImages_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EntityImages_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EntityImages_Places_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "Places",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EntityImages_Regions_RegionId",
                        column: x => x.RegionId,
                        principalTable: "Regions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EntityImages_Territories_TerritoryId",
                        column: x => x.TerritoryId,
                        principalTable: "Territories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventReviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    EventId = table.Column<int>(type: "integer", nullable: false),
                    Rating = table.Column<double>(type: "double precision", nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventReviews_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventReviews_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventTags",
                columns: table => new
                {
                    EventId = table.Column<int>(type: "integer", nullable: false),
                    TagId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventTags", x => new { x.EventId, x.TagId });
                    table.ForeignKey(
                        name: "FK_EventTags_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlaceContributionImages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlaceContributionId = table.Column<int>(type: "integer", nullable: false),
                    Url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    AltText = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Caption = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsMain = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ImagePosition = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ImageSize = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaceContributionImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaceContributionImages_PlaceContributions_PlaceContributio~",
                        column: x => x.PlaceContributionId,
                        principalTable: "PlaceContributions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StayStayAmenities",
                columns: table => new
                {
                    StayPlaceId = table.Column<int>(type: "integer", nullable: false),
                    StayAmenityId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StayStayAmenities", x => new { x.StayPlaceId, x.StayAmenityId });
                    table.ForeignKey(
                        name: "FK_StayStayAmenities_StayAmenities_StayAmenityId",
                        column: x => x.StayAmenityId,
                        principalTable: "StayAmenities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StayStayAmenities_Stays_StayPlaceId",
                        column: x => x.StayPlaceId,
                        principalTable: "Stays",
                        principalColumn: "PlaceId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReviewReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TargetType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ReviewId = table.Column<int>(type: "integer", nullable: false),
                    PlaceReviewId = table.Column<int>(type: "integer", nullable: true),
                    EventReviewId = table.Column<int>(type: "integer", nullable: true),
                    ReporterUserId = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ReviewedByUserId = table.Column<int>(type: "integer", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Decision = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    DecisionNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ReviewAuthorUserId = table.Column<int>(type: "integer", nullable: false),
                    ReviewAuthorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EntityId = table.Column<int>(type: "integer", nullable: false),
                    EntityName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RegionId = table.Column<int>(type: "integer", nullable: false),
                    RegionName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ReviewRating = table.Column<double>(type: "double precision", nullable: false),
                    ReviewCommentSnapshot = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ReviewCreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewReports_EventReviews_EventReviewId",
                        column: x => x.EventReviewId,
                        principalTable: "EventReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ReviewReports_PlaceReviews_PlaceReviewId",
                        column: x => x.PlaceReviewId,
                        principalTable: "PlaceReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ReviewReports_Users_ReporterUserId",
                        column: x => x.ReporterUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReviewReports_Users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Amenities_Name",
                table: "Amenities",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_AnonymousSessionId_OccurredAt",
                table: "AnalyticsEvents",
                columns: new[] { "AnonymousSessionId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_DestinationId_OccurredAt",
                table: "AnalyticsEvents",
                columns: new[] { "DestinationId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_EntityType_EntityId_OccurredAt",
                table: "AnalyticsEvents",
                columns: new[] { "EntityType", "EntityId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_EventType_OccurredAt",
                table: "AnalyticsEvents",
                columns: new[] { "EventType", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_OccurredAt",
                table: "AnalyticsEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_UserId_OccurredAt",
                table: "AnalyticsEvents",
                columns: new[] { "UserId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AnonymousSessions_LastSeenAt",
                table: "AnonymousSessions",
                column: "LastSeenAt");

            migrationBuilder.CreateIndex(
                name: "IX_BookingPartners_Name",
                table: "BookingPartners",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CollectionItems_CollectionId",
                table: "CollectionItems",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionItems_DestinationId",
                table: "CollectionItems",
                column: "DestinationId");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionItems_EventId",
                table: "CollectionItems",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionItems_LocationId",
                table: "CollectionItems",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionItems_PlaceId",
                table: "CollectionItems",
                column: "PlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Collections_UserId",
                table: "Collections",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_DestinationReviews_DestinationId",
                table: "DestinationReviews",
                column: "DestinationId");

            migrationBuilder.CreateIndex(
                name: "IX_DestinationReviews_UserId",
                table: "DestinationReviews",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_DestinationReviews_UserId_DestinationId",
                table: "DestinationReviews",
                columns: new[] { "UserId", "DestinationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Destinations_Coordinates",
                table: "Destinations",
                column: "Coordinates")
                .Annotation("Npgsql:IndexMethod", "GIST");

            migrationBuilder.CreateIndex(
                name: "IX_Destinations_DestinationTypeId",
                table: "Destinations",
                column: "DestinationTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Destinations_RegionId",
                table: "Destinations",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_DestinationTags_TagId",
                table: "DestinationTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_DestinationTypes_Name",
                table: "DestinationTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EntityImages_DestinationId",
                table: "EntityImages",
                column: "DestinationId",
                unique: true,
                filter: "\"DestinationId\" IS NOT NULL AND \"IsMain\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_EntityImages_EventId",
                table: "EntityImages",
                column: "EventId",
                unique: true,
                filter: "\"EventId\" IS NOT NULL AND \"IsMain\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_EntityImages_LocationId",
                table: "EntityImages",
                column: "LocationId",
                unique: true,
                filter: "\"LocationId\" IS NOT NULL AND \"IsMain\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_EntityImages_PlaceId",
                table: "EntityImages",
                column: "PlaceId",
                unique: true,
                filter: "\"PlaceId\" IS NOT NULL AND \"IsMain\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_EntityImages_RegionId",
                table: "EntityImages",
                column: "RegionId",
                unique: true,
                filter: "\"RegionId\" IS NOT NULL AND \"IsMain\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_EntityImages_TerritoryId",
                table: "EntityImages",
                column: "TerritoryId",
                unique: true,
                filter: "\"TerritoryId\" IS NOT NULL AND \"IsMain\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_EventReviews_EventId",
                table: "EventReviews",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_EventReviews_UserId",
                table: "EventReviews",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_EventReviews_UserId_EventId",
                table: "EventReviews",
                columns: new[] { "UserId", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_Coordinates",
                table: "Events",
                column: "Coordinates")
                .Annotation("Npgsql:IndexMethod", "GIST");

            migrationBuilder.CreateIndex(
                name: "IX_Events_EventTypeId",
                table: "Events",
                column: "EventTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Events_LocationId",
                table: "Events",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Events_PlaceId",
                table: "Events",
                column: "PlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_EventTags_TagId",
                table: "EventTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_EventTypes_Name",
                table: "EventTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Favorites_UserId_EntityType_EntityId",
                table: "Favorites",
                columns: new[] { "UserId", "EntityType", "EntityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LocationReviews_LocationId",
                table: "LocationReviews",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_LocationReviews_UserId",
                table: "LocationReviews",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_LocationReviews_UserId_LocationId",
                table: "LocationReviews",
                columns: new[] { "UserId", "LocationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Locations_Coordinates",
                table: "Locations",
                column: "Coordinates")
                .Annotation("Npgsql:IndexMethod", "GIST");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_DestinationId",
                table: "Locations",
                column: "DestinationId");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_LocationTypeId",
                table: "Locations",
                column: "LocationTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_LocationTags_TagId",
                table: "LocationTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_LocationTypes_Name",
                table: "LocationTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModerationTerms_Category",
                table: "ModerationTerms",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationTerms_CreatedByUserId",
                table: "ModerationTerms",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationTerms_IsActive_LanguageCode",
                table: "ModerationTerms",
                columns: new[] { "IsActive", "LanguageCode" });

            migrationBuilder.CreateIndex(
                name: "IX_ModerationTerms_MatchType",
                table: "ModerationTerms",
                column: "MatchType");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationTerms_UpdatedByUserId",
                table: "ModerationTerms",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationTerms_UQ_NormalizedRule",
                table: "ModerationTerms",
                columns: new[] { "LanguageCode", "NormalizedTerm", "Category", "MatchType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlaceAmenities_AmenityId",
                table: "PlaceAmenities",
                column: "AmenityId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceBookingPartners_BookingPartnerId",
                table: "PlaceBookingPartners",
                column: "BookingPartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceContributionImages_PlaceContributionId",
                table: "PlaceContributionImages",
                column: "PlaceContributionId",
                unique: true,
                filter: "\"IsMain\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceContributions_LocationId",
                table: "PlaceContributions",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceContributions_PlaceId",
                table: "PlaceContributions",
                column: "PlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceContributions_PlaceTypeId",
                table: "PlaceContributions",
                column: "PlaceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceContributions_RestaurantTypeId",
                table: "PlaceContributions",
                column: "RestaurantTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceContributions_ReviewedByUserId",
                table: "PlaceContributions",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceContributions_Status_SubmittedAt",
                table: "PlaceContributions",
                columns: new[] { "Status", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PlaceContributions_StayTypeId",
                table: "PlaceContributions",
                column: "StayTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceContributions_Type_Status",
                table: "PlaceContributions",
                columns: new[] { "Type", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PlaceContributions_UserId",
                table: "PlaceContributions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceReviews_PlaceId",
                table: "PlaceReviews",
                column: "PlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceReviews_UserId",
                table: "PlaceReviews",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceReviews_UserId_PlaceId",
                table: "PlaceReviews",
                columns: new[] { "UserId", "PlaceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Places_Coordinates",
                table: "Places",
                column: "Coordinates")
                .Annotation("Npgsql:IndexMethod", "GIST");

            migrationBuilder.CreateIndex(
                name: "IX_Places_LocationId",
                table: "Places",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Places_PlaceTypeId",
                table: "Places",
                column: "PlaceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceTags_TagId",
                table: "PlaceTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceTypes_Name",
                table: "PlaceTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId_IsRevoked_ExpiresAt",
                table: "RefreshTokens",
                columns: new[] { "UserId", "IsRevoked", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RegionalManagerRegions_RegionId",
                table: "RegionalManagerRegions",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_Regions_TerritoryId",
                table: "Regions",
                column: "TerritoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Regions_TerritoryId_Name",
                table: "Regions",
                columns: new[] { "TerritoryId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantTypes_Name",
                table: "RestaurantTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantTypes_Slug",
                table: "RestaurantTypes",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReviewReports_EventReviewId",
                table: "ReviewReports",
                column: "EventReviewId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewReports_PlaceReviewId",
                table: "ReviewReports",
                column: "PlaceReviewId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewReports_RegionId_Status_CreatedAt",
                table: "ReviewReports",
                columns: new[] { "RegionId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewReports_ReporterUserId_TargetType_ReviewId",
                table: "ReviewReports",
                columns: new[] { "ReporterUserId", "TargetType", "ReviewId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReviewReports_ReviewedByUserId",
                table: "ReviewReports",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewReports_Status",
                table: "ReviewReports",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewReports_TargetType_ReviewId",
                table: "ReviewReports",
                columns: new[] { "TargetType", "ReviewId" });

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Name",
                table: "Roles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StayAmenities_Name",
                table: "StayAmenities",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StayAmenities_Slug",
                table: "StayAmenities",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stays_StayTypeId",
                table: "Stays",
                column: "StayTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_StayStayAmenities_StayAmenityId",
                table: "StayStayAmenities",
                column: "StayAmenityId");

            migrationBuilder.CreateIndex(
                name: "IX_StayTypes_Name",
                table: "StayTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StayTypes_Slug",
                table: "StayTypes",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportRequests_UserId_CreatedAt",
                table: "SupportRequests",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Name",
                table: "Tags",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tags_PlaceTypeId",
                table: "Tags",
                column: "PlaceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Slug",
                table: "Tags",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TagTypes_Name",
                table: "TagTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TagTypes_Slug",
                table: "TagTypes",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Territories_Name",
                table: "Territories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TerritoryManagerTerritories_TerritoryId",
                table: "TerritoryManagerTerritories",
                column: "TerritoryId");

            migrationBuilder.CreateIndex(
                name: "IX_UserInterestSignals_TagId",
                table: "UserInterestSignals",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_UserInterestSignals_UserId_Score",
                table: "UserInterestSignals",
                columns: new[] { "UserId", "Score" });

            migrationBuilder.CreateIndex(
                name: "IX_UserInterestSignals_UserId_TagId",
                table: "UserInterestSignals",
                columns: new[] { "UserId", "TagId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserNotifications_RelatedEntityType_RelatedEntityId",
                table: "UserNotifications",
                columns: new[] { "RelatedEntityType", "RelatedEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserNotifications_UserId_CreatedAt",
                table: "UserNotifications",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UserNotifications_UserId_ReadAt",
                table: "UserNotifications",
                columns: new[] { "UserId", "ReadAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UserPlaces_PlaceId",
                table: "UserPlaces",
                column: "PlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_RoleId",
                table: "UserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserTags_TagId",
                table: "UserTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_UserTags_UserId_TagId",
                table: "UserTags",
                columns: new[] { "UserId", "TagId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnalyticsEvents");

            migrationBuilder.DropTable(
                name: "CollectionItems");

            migrationBuilder.DropTable(
                name: "DestinationReviews");

            migrationBuilder.DropTable(
                name: "DestinationTags");

            migrationBuilder.DropTable(
                name: "EntityImages");

            migrationBuilder.DropTable(
                name: "EventTags");

            migrationBuilder.DropTable(
                name: "Favorites");

            migrationBuilder.DropTable(
                name: "LocationReviews");

            migrationBuilder.DropTable(
                name: "LocationTags");

            migrationBuilder.DropTable(
                name: "ModerationTerms");

            migrationBuilder.DropTable(
                name: "PlaceAmenities");

            migrationBuilder.DropTable(
                name: "PlaceBookingPartners");

            migrationBuilder.DropTable(
                name: "PlaceContributionImages");

            migrationBuilder.DropTable(
                name: "PlaceTags");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "RegionalManagerRegions");

            migrationBuilder.DropTable(
                name: "ReviewReports");

            migrationBuilder.DropTable(
                name: "StayStayAmenities");

            migrationBuilder.DropTable(
                name: "SupportRequests");

            migrationBuilder.DropTable(
                name: "TagTypes");

            migrationBuilder.DropTable(
                name: "TerritoryManagerTerritories");

            migrationBuilder.DropTable(
                name: "UserInterestSignals");

            migrationBuilder.DropTable(
                name: "UserNotifications");

            migrationBuilder.DropTable(
                name: "UserPlaces");

            migrationBuilder.DropTable(
                name: "UserRoles");

            migrationBuilder.DropTable(
                name: "UserTags");

            migrationBuilder.DropTable(
                name: "AnonymousSessions");

            migrationBuilder.DropTable(
                name: "Collections");

            migrationBuilder.DropTable(
                name: "Amenities");

            migrationBuilder.DropTable(
                name: "BookingPartners");

            migrationBuilder.DropTable(
                name: "PlaceContributions");

            migrationBuilder.DropTable(
                name: "RegionalManagers");

            migrationBuilder.DropTable(
                name: "EventReviews");

            migrationBuilder.DropTable(
                name: "PlaceReviews");

            migrationBuilder.DropTable(
                name: "StayAmenities");

            migrationBuilder.DropTable(
                name: "Stays");

            migrationBuilder.DropTable(
                name: "TerritoryManagers");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "RestaurantTypes");

            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropTable(
                name: "StayTypes");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "EventTypes");

            migrationBuilder.DropTable(
                name: "Places");

            migrationBuilder.DropTable(
                name: "Locations");

            migrationBuilder.DropTable(
                name: "PlaceTypes");

            migrationBuilder.DropTable(
                name: "Destinations");

            migrationBuilder.DropTable(
                name: "LocationTypes");

            migrationBuilder.DropTable(
                name: "DestinationTypes");

            migrationBuilder.DropTable(
                name: "Regions");

            migrationBuilder.DropTable(
                name: "Territories");
        }
    }
}
