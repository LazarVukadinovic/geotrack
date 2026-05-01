using Geotrack.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260427220000_AddUserInterestSignalDismissal")]
    public partial class AddUserInterestSignalDismissal : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "UserInterestSignals"
                ADD COLUMN IF NOT EXISTS "DismissedAt" timestamp with time zone;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "UserInterestSignals"
                ADD COLUMN IF NOT EXISTS "DismissedAtScore" double precision;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "UserInterestSignals"
                ADD COLUMN IF NOT EXISTS "EvidenceSinceDismissal" double precision NOT NULL DEFAULT 0.0;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "UserInterestSignals"
                ADD COLUMN IF NOT EXISTS "IsDismissed" boolean NOT NULL DEFAULT FALSE;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""ALTER TABLE "UserInterestSignals" DROP COLUMN IF EXISTS "DismissedAt";""");
            migrationBuilder.Sql("""ALTER TABLE "UserInterestSignals" DROP COLUMN IF EXISTS "DismissedAtScore";""");
            migrationBuilder.Sql("""ALTER TABLE "UserInterestSignals" DROP COLUMN IF EXISTS "EvidenceSinceDismissal";""");
            migrationBuilder.Sql("""ALTER TABLE "UserInterestSignals" DROP COLUMN IF EXISTS "IsDismissed";""");
        }
    }
}
