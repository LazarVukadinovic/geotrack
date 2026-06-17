using Geotrack.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260617050000_EnablePlaceAndLocationTagFilters")]
    public partial class EnablePlaceAndLocationTagFilters : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "TagTypes"
                SET "IsFilterable" = TRUE,
                    "FilterLabel" = CASE "Slug"
                        WHEN 'location-character' THEN 'Character'
                        WHEN 'cuisine' THEN 'Cuisine'
                        WHEN 'dining-style' THEN 'Dining style'
                        WHEN 'stay-style' THEN 'Stay style'
                        WHEN 'activity' THEN 'Activities'
                        WHEN 'explore-topic' THEN 'Topics'
                        WHEN 'setting' THEN 'Setting'
                        WHEN 'price-level' THEN 'Price level'
                        ELSE "FilterLabel"
                    END,
                    "FilterOrder" = CASE "Slug"
                        WHEN 'location-character' THEN 20
                        WHEN 'cuisine' THEN 20
                        WHEN 'dining-style' THEN 30
                        WHEN 'stay-style' THEN 40
                        WHEN 'activity' THEN 50
                        WHEN 'explore-topic' THEN 60
                        WHEN 'setting' THEN 70
                        WHEN 'price-level' THEN 80
                        ELSE "FilterOrder"
                    END
                WHERE "Slug" IN (
                    'location-character',
                    'cuisine',
                    'dining-style',
                    'stay-style',
                    'activity',
                    'explore-topic',
                    'setting',
                    'price-level'
                );
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "TagTypes"
                SET "IsFilterable" = FALSE,
                    "FilterLabel" = NULL,
                    "FilterOrder" = 0
                WHERE "Slug" IN (
                    'location-character',
                    'cuisine',
                    'dining-style',
                    'stay-style',
                    'activity',
                    'explore-topic',
                    'setting',
                    'price-level'
                );
                """);
        }
    }
}
