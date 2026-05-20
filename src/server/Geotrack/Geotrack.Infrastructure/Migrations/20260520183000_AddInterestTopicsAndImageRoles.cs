using System;
using Geotrack.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260520183000_AddInterestTopicsAndImageRoles")]
    public partial class AddInterestTopicsAndImageRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageRole",
                table: "PlaceContributionImages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "gallery");

            migrationBuilder.AddColumn<bool>(
                name: "IsOwnable",
                table: "PlaceTypes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ImageRole",
                table: "EntityImages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "gallery");

            migrationBuilder.CreateTable(
                name: "InterestTopics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Group = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    IconKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ShowOnOnboarding = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Slug = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterestTopics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InterestTopicTags",
                columns: table => new
                {
                    InterestTopicId = table.Column<int>(type: "integer", nullable: false),
                    TagId = table.Column<int>(type: "integer", nullable: false),
                    Weight = table.Column<double>(type: "double precision", nullable: false, defaultValue: 1.0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterestTopicTags", x => new { x.InterestTopicId, x.TagId });
                    table.ForeignKey(
                        name: "FK_InterestTopicTags_InterestTopics_InterestTopicId",
                        column: x => x.InterestTopicId,
                        principalTable: "InterestTopics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InterestTopicTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserInterestTopics",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    InterestTopicId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserInterestTopics", x => new { x.UserId, x.InterestTopicId });
                    table.ForeignKey(
                        name: "FK_UserInterestTopics_InterestTopics_InterestTopicId",
                        column: x => x.InterestTopicId,
                        principalTable: "InterestTopics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserInterestTopics_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserInterestTopicSignals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DismissedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DismissedAtScore = table.Column<double>(type: "double precision", nullable: true),
                    EvidenceSinceDismissal = table.Column<double>(type: "double precision", nullable: false, defaultValue: 0.0),
                    InterestTopicId = table.Column<int>(type: "integer", nullable: false),
                    IsDismissed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    LastInteractionAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastReason = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Score = table.Column<double>(type: "double precision", nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "inferred"),
                    UserId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserInterestTopicSignals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserInterestTopicSignals_InterestTopics_InterestTopicId",
                        column: x => x.InterestTopicId,
                        principalTable: "InterestTopics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserInterestTopicSignals_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InterestTopics_IsActive_ShowOnOnboarding_SortOrder",
                table: "InterestTopics",
                columns: new[] { "IsActive", "ShowOnOnboarding", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_InterestTopics_Slug",
                table: "InterestTopics",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InterestTopicTags_TagId",
                table: "InterestTopicTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_UserInterestTopics_InterestTopicId",
                table: "UserInterestTopics",
                column: "InterestTopicId");

            migrationBuilder.CreateIndex(
                name: "IX_UserInterestTopicSignals_InterestTopicId",
                table: "UserInterestTopicSignals",
                column: "InterestTopicId");

            migrationBuilder.CreateIndex(
                name: "IX_UserInterestTopicSignals_UserId_InterestTopicId",
                table: "UserInterestTopicSignals",
                columns: new[] { "UserId", "InterestTopicId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserInterestTopicSignals_UserId_Score",
                table: "UserInterestTopicSignals",
                columns: new[] { "UserId", "Score" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InterestTopicTags");

            migrationBuilder.DropTable(
                name: "UserInterestTopics");

            migrationBuilder.DropTable(
                name: "UserInterestTopicSignals");

            migrationBuilder.DropTable(
                name: "InterestTopics");

            migrationBuilder.DropColumn(
                name: "ImageRole",
                table: "PlaceContributionImages");

            migrationBuilder.DropColumn(
                name: "IsOwnable",
                table: "PlaceTypes");

            migrationBuilder.DropColumn(
                name: "ImageRole",
                table: "EntityImages");
        }
    }
}
