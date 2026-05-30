using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchTextColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .Annotation("Npgsql:PostgresExtension:postgis", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.AddColumn<string>(
                name: "SearchText",
                table: "Places",
                type: "character varying(12000)",
                maxLength: 12000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SearchText",
                table: "Locations",
                type: "character varying(12000)",
                maxLength: 12000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SearchText",
                table: "Events",
                type: "character varying(12000)",
                maxLength: 12000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SearchText",
                table: "Destinations",
                type: "character varying(12000)",
                maxLength: 12000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE "Destinations"
                SET "SearchText" = trim(regexp_replace(
                    translate(replace(lower(concat_ws(' ', "Name", "Summary", "Description", "InternalReferenceId")), 'đ', 'dj'), 'ščćž', 'sccz'),
                    '[^a-z0-9]+',
                    ' ',
                    'g'));

                UPDATE "Locations"
                SET "SearchText" = trim(regexp_replace(
                    translate(replace(lower(concat_ws(' ', "Name", "Summary", "Description", "InternalReferenceId")), 'đ', 'dj'), 'ščćž', 'sccz'),
                    '[^a-z0-9]+',
                    ' ',
                    'g'));

                UPDATE "Places"
                SET "SearchText" = trim(regexp_replace(
                    translate(replace(lower(concat_ws(' ', "Name", "Subtitle", "Address", "Description", "InternalReferenceId")), 'đ', 'dj'), 'ščćž', 'sccz'),
                    '[^a-z0-9]+',
                    ' ',
                    'g'));

                UPDATE "Events"
                SET "SearchText" = trim(regexp_replace(
                    translate(replace(lower(concat_ws(' ', "Name", "Summary", "Description", "VenueName", "InternalReferenceId")), 'đ', 'dj'), 'ščćž', 'sccz'),
                    '[^a-z0-9]+',
                    ' ',
                    'g'));
                """);

            migrationBuilder.Sql("""
                CREATE INDEX "IX_Destinations_SearchText_Trgm"
                    ON "Destinations" USING gin ("SearchText" gin_trgm_ops);
                CREATE INDEX "IX_Locations_SearchText_Trgm"
                    ON "Locations" USING gin ("SearchText" gin_trgm_ops);
                CREATE INDEX "IX_Places_SearchText_Trgm"
                    ON "Places" USING gin ("SearchText" gin_trgm_ops);
                CREATE INDEX "IX_Events_SearchText_Trgm"
                    ON "Events" USING gin ("SearchText" gin_trgm_ops);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_Events_SearchText_Trgm";
                DROP INDEX IF EXISTS "IX_Places_SearchText_Trgm";
                DROP INDEX IF EXISTS "IX_Locations_SearchText_Trgm";
                DROP INDEX IF EXISTS "IX_Destinations_SearchText_Trgm";
                """);

            migrationBuilder.DropColumn(
                name: "SearchText",
                table: "Places");

            migrationBuilder.DropColumn(
                name: "SearchText",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "SearchText",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "SearchText",
                table: "Destinations");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:postgis", ",,");
        }
    }
}
