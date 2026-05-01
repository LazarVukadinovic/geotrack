using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewReportsAndModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReviewReports");
        }
    }
}
