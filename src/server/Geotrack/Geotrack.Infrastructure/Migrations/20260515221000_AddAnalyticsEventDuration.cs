using Geotrack.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260515221000_AddAnalyticsEventDuration")]
    public partial class AddAnalyticsEventDuration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'AnalyticsEvents'
                          AND column_name = 'DurationMs'
                    ) THEN
                        ALTER TABLE "AnalyticsEvents" ADD COLUMN "DurationMs" integer;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'AnalyticsEvents'
                          AND column_name = 'DurationMs'
                    ) THEN
                        ALTER TABLE "AnalyticsEvents" DROP COLUMN "DurationMs";
                    END IF;
                END $$;
                """);
        }
    }
}
