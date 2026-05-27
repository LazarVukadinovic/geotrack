using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportTicketAdminWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AssignedAdminId",
                table: "SupportRequests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentUrl",
                table: "SupportRequests",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Browser",
                table: "SupportRequests",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeviceInfo",
                table: "SupportRequests",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InternalNote",
                table: "SupportRequests",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PageUrl",
                table: "SupportRequests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Platform",
                table: "SupportRequests",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Priority",
                table: "SupportRequests",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Normal");

            migrationBuilder.AddColumn<string>(
                name: "ResolutionMessage",
                table: "SupportRequests",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedAt",
                table: "SupportRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResolvedByUserId",
                table: "SupportRequests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserAgent",
                table: "SupportRequests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportRequests_AssignedAdminId",
                table: "SupportRequests",
                column: "AssignedAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportRequests_Priority",
                table: "SupportRequests",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "IX_SupportRequests_ResolvedByUserId",
                table: "SupportRequests",
                column: "ResolvedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportRequests_Status_CreatedAt",
                table: "SupportRequests",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_SupportRequests_Users_AssignedAdminId",
                table: "SupportRequests",
                column: "AssignedAdminId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_SupportRequests_Users_ResolvedByUserId",
                table: "SupportRequests",
                column: "ResolvedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SupportRequests_Users_AssignedAdminId",
                table: "SupportRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_SupportRequests_Users_ResolvedByUserId",
                table: "SupportRequests");

            migrationBuilder.DropIndex(
                name: "IX_SupportRequests_AssignedAdminId",
                table: "SupportRequests");

            migrationBuilder.DropIndex(
                name: "IX_SupportRequests_Priority",
                table: "SupportRequests");

            migrationBuilder.DropIndex(
                name: "IX_SupportRequests_ResolvedByUserId",
                table: "SupportRequests");

            migrationBuilder.DropIndex(
                name: "IX_SupportRequests_Status_CreatedAt",
                table: "SupportRequests");

            migrationBuilder.DropColumn(
                name: "AssignedAdminId",
                table: "SupportRequests");

            migrationBuilder.DropColumn(
                name: "AttachmentUrl",
                table: "SupportRequests");

            migrationBuilder.DropColumn(
                name: "Browser",
                table: "SupportRequests");

            migrationBuilder.DropColumn(
                name: "DeviceInfo",
                table: "SupportRequests");

            migrationBuilder.DropColumn(
                name: "InternalNote",
                table: "SupportRequests");

            migrationBuilder.DropColumn(
                name: "PageUrl",
                table: "SupportRequests");

            migrationBuilder.DropColumn(
                name: "Platform",
                table: "SupportRequests");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "SupportRequests");

            migrationBuilder.DropColumn(
                name: "ResolutionMessage",
                table: "SupportRequests");

            migrationBuilder.DropColumn(
                name: "ResolvedAt",
                table: "SupportRequests");

            migrationBuilder.DropColumn(
                name: "ResolvedByUserId",
                table: "SupportRequests");

            migrationBuilder.DropColumn(
                name: "UserAgent",
                table: "SupportRequests");

        }
    }
}
