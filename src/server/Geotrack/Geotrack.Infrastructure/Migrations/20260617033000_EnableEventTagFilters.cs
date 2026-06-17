using Geotrack.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260617033000_EnableEventTagFilters")]
    public partial class EnableEventTagFilters : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "TagTypes"
                SET "IsFilterable" = TRUE,
                    "FilterLabel" = CASE "Slug"
                        WHEN 'event-category' THEN 'Event category'
                        WHEN 'event-access' THEN 'Access'
                        ELSE "FilterLabel"
                    END,
                    "FilterOrder" = CASE "Slug"
                        WHEN 'event-category' THEN 20
                        WHEN 'event-access' THEN 30
                        ELSE "FilterOrder"
                    END
                WHERE "Slug" IN ('event-category', 'event-access');
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "TagTypes"
                SET "IsFilterable" = FALSE,
                    "FilterLabel" = NULL,
                    "FilterOrder" = 0
                WHERE "Slug" IN ('event-category', 'event-access');
                """);
        }
    }
}
