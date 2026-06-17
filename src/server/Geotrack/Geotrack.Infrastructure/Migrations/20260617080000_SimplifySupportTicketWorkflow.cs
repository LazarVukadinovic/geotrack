using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    public partial class SimplifySupportTicketWorkflow : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "SupportRequests"
                SET "Status" = 'Open',
                    "UpdatedAt" = COALESCE("UpdatedAt", NOW())
                WHERE "Status" IN ('Received', 'InProgress');
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_SupportRequests_Users_AssignedAdminId",
                table: "SupportRequests");

            migrationBuilder.DropIndex(
                name: "IX_SupportRequests_AssignedAdminId",
                table: "SupportRequests");

            migrationBuilder.DropColumn(
                name: "AssignedAdminId",
                table: "SupportRequests");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AssignedAdminId",
                table: "SupportRequests",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportRequests_AssignedAdminId",
                table: "SupportRequests",
                column: "AssignedAdminId");

            migrationBuilder.AddForeignKey(
                name: "FK_SupportRequests_Users_AssignedAdminId",
                table: "SupportRequests",
                column: "AssignedAdminId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
