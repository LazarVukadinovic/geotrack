using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260527120000_AddAdminNotificationScopes")]
    public partial class AddAdminNotificationScopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AdminNotifications_Type_TargetType_TargetId",
                table: "AdminNotifications");

            migrationBuilder.AddColumn<int>(
                name: "ScopeId",
                table: "AdminNotifications",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScopeLabel",
                table: "AdminNotifications",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScopeType",
                table: "AdminNotifications",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "Global");

            migrationBuilder.CreateIndex(
                name: "IX_AdminNotifications_AudienceRole_ScopeType_ScopeId_CreatedAt",
                table: "AdminNotifications",
                columns: new[] { "AudienceRole", "ScopeType", "ScopeId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminNotifications_AudienceRole_Type_TargetType_TargetId_ScopeType_ScopeId",
                table: "AdminNotifications",
                columns: new[] { "AudienceRole", "Type", "TargetType", "TargetId", "ScopeType", "ScopeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AdminNotifications_AudienceRole_ScopeType_ScopeId_CreatedAt",
                table: "AdminNotifications");

            migrationBuilder.DropIndex(
                name: "IX_AdminNotifications_AudienceRole_Type_TargetType_TargetId_ScopeType_ScopeId",
                table: "AdminNotifications");

            migrationBuilder.DropColumn(
                name: "ScopeId",
                table: "AdminNotifications");

            migrationBuilder.DropColumn(
                name: "ScopeLabel",
                table: "AdminNotifications");

            migrationBuilder.DropColumn(
                name: "ScopeType",
                table: "AdminNotifications");

            migrationBuilder.CreateIndex(
                name: "IX_AdminNotifications_Type_TargetType_TargetId",
                table: "AdminNotifications",
                columns: new[] { "Type", "TargetType", "TargetId" },
                unique: true);
        }
    }
}
