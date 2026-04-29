using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDatabaseBackedModerationTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ModerationTerms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Term = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    NormalizedTerm = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    LanguageCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    MatchType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModerationTerms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModerationTerms_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ModerationTerms_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ModerationTerms_Category",
                table: "ModerationTerms",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationTerms_CreatedByUserId",
                table: "ModerationTerms",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationTerms_IsActive_LanguageCode",
                table: "ModerationTerms",
                columns: new[] { "IsActive", "LanguageCode" });

            migrationBuilder.CreateIndex(
                name: "IX_ModerationTerms_MatchType",
                table: "ModerationTerms",
                column: "MatchType");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationTerms_UpdatedByUserId",
                table: "ModerationTerms",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationTerms_UQ_NormalizedRule",
                table: "ModerationTerms",
                columns: new[] { "LanguageCode", "NormalizedTerm", "Category", "MatchType" },
                unique: true);

            var seededAt = new DateTime(2026, 4, 29, 18, 16, 58, DateTimeKind.Utc);

            migrationBuilder.InsertData(
                table: "ModerationTerms",
                columns: new[]
                {
                    "Term",
                    "NormalizedTerm",
                    "LanguageCode",
                    "Category",
                    "MatchType",
                    "IsActive",
                    "Severity",
                    "CreatedAt"
                },
                values: new object[,]
                {
                    { "fuck", "fuck", "en", "Profanity", "Exact", true, 4, seededAt },
                    { "fucks", "fucks", "en", "Profanity", "Exact", true, 4, seededAt },
                    { "fucked", "fucked", "en", "Profanity", "Exact", true, 4, seededAt },
                    { "fucker", "fucker", "en", "Profanity", "Exact", true, 4, seededAt },
                    { "fuckers", "fuckers", "en", "Profanity", "Exact", true, 4, seededAt },
                    { "fucking", "fucking", "en", "Profanity", "Exact", true, 4, seededAt },
                    { "fck", "fck", "en", "Profanity", "Exact", true, 4, seededAt },
                    { "shit", "shit", "en", "Profanity", "Exact", true, 3, seededAt },
                    { "shitty", "shitty", "en", "Profanity", "Exact", true, 3, seededAt },
                    { "bullshit", "bullshit", "en", "Profanity", "Exact", true, 3, seededAt },
                    { "bitch", "bitch", "en", "Harassment", "Exact", true, 3, seededAt },
                    { "bitches", "bitches", "en", "Harassment", "Exact", true, 3, seededAt },
                    { "bastard", "bastard", "en", "Harassment", "Exact", true, 3, seededAt },
                    { "bastards", "bastards", "en", "Harassment", "Exact", true, 3, seededAt },
                    { "asshole", "asshole", "en", "Harassment", "Exact", true, 3, seededAt },
                    { "assholes", "assholes", "en", "Harassment", "Exact", true, 3, seededAt },
                    { "dick", "dick", "en", "Profanity", "Exact", true, 3, seededAt },
                    { "dicks", "dicks", "en", "Profanity", "Exact", true, 3, seededAt },
                    { "cunt", "cunt", "en", "Profanity", "Exact", true, 5, seededAt },
                    { "cunts", "cunts", "en", "Profanity", "Exact", true, 5, seededAt },
                    { "slut", "slut", "en", "Harassment", "Exact", true, 4, seededAt },
                    { "sluts", "sluts", "en", "Harassment", "Exact", true, 4, seededAt },
                    { "whore", "whore", "en", "Harassment", "Exact", true, 4, seededAt },
                    { "whores", "whores", "en", "Harassment", "Exact", true, 4, seededAt },
                    { "prick", "prick", "en", "Harassment", "Exact", true, 3, seededAt },
                    { "pricks", "pricks", "en", "Harassment", "Exact", true, 3, seededAt },
                    { "idiot", "idiot", "en", "Harassment", "Exact", true, 2, seededAt },
                    { "idiots", "idiots", "en", "Harassment", "Exact", true, 2, seededAt },
                    { "moron", "moron", "en", "Harassment", "Exact", true, 2, seededAt },
                    { "morons", "morons", "en", "Harassment", "Exact", true, 2, seededAt },
                    { "retard", "retard", "en", "Slur", "Exact", true, 4, seededAt },
                    { "retards", "retards", "en", "Slur", "Exact", true, 4, seededAt },
                    { "nigger", "nigger", "en", "Slur", "Exact", true, 5, seededAt },
                    { "niggers", "niggers", "en", "Slur", "Exact", true, 5, seededAt },
                    { "nigga", "nigga", "en", "Slur", "Exact", true, 5, seededAt },
                    { "niggas", "niggas", "en", "Slur", "Exact", true, 5, seededAt },
                    { "faggot", "faggot", "en", "Slur", "Exact", true, 5, seededAt },
                    { "faggots", "faggots", "en", "Slur", "Exact", true, 5, seededAt },
                    { "kike", "kike", "en", "Slur", "Exact", true, 5, seededAt },

                    { "fuck", "fuck", "en", "Profanity", "Prefix", true, 4, seededAt },
                    { "shit", "shit", "en", "Profanity", "Prefix", true, 3, seededAt },
                    { "asshole", "asshole", "en", "Harassment", "Prefix", true, 3, seededAt },
                    { "bitch", "bitch", "en", "Harassment", "Prefix", true, 3, seededAt },
                    { "bastard", "bastard", "en", "Harassment", "Prefix", true, 3, seededAt },
                    { "cunt", "cunt", "en", "Profanity", "Prefix", true, 5, seededAt },
                    { "whore", "whore", "en", "Harassment", "Prefix", true, 4, seededAt },
                    { "faggot", "faggot", "en", "Slur", "Prefix", true, 5, seededAt },

                    { "jebem", "jebem", "sr", "Profanity", "Exact", true, 4, seededAt },
                    { "jebes", "jebes", "sr", "Profanity", "Exact", true, 4, seededAt },
                    { "jebe", "jebe", "sr", "Profanity", "Exact", true, 4, seededAt },
                    { "jebi", "jebi", "sr", "Profanity", "Exact", true, 4, seededAt },
                    { "jebite", "jebite", "sr", "Profanity", "Exact", true, 4, seededAt },
                    { "jebote", "jebote", "sr", "Profanity", "Exact", true, 4, seededAt },
                    { "jebiga", "jebiga", "sr", "Profanity", "Exact", true, 4, seededAt },
                    { "jebeno", "jebeno", "sr", "Profanity", "Exact", true, 4, seededAt },
                    { "jebena", "jebena", "sr", "Profanity", "Exact", true, 4, seededAt },
                    { "jebeni", "jebeni", "sr", "Profanity", "Exact", true, 4, seededAt },
                    { "jebo", "jebo", "sr", "Profanity", "Exact", true, 4, seededAt },
                    { "jebao", "jebao", "sr", "Profanity", "Exact", true, 4, seededAt },
                    { "kurac", "kurac", "sr", "Profanity", "Exact", true, 4, seededAt },
                    { "kurcem", "kurcem", "sr", "Profanity", "Exact", true, 4, seededAt },
                    { "kurcu", "kurcu", "sr", "Profanity", "Exact", true, 4, seededAt },
                    { "kurcina", "kurcina", "sr", "Profanity", "Exact", true, 4, seededAt },
                    { "kurcinu", "kurcinu", "sr", "Profanity", "Exact", true, 4, seededAt },
                    { "picka", "picka", "sr", "Profanity", "Exact", true, 4, seededAt },
                    { "picke", "picke", "sr", "Profanity", "Exact", true, 4, seededAt },
                    { "picku", "picku", "sr", "Profanity", "Exact", true, 4, seededAt },
                    { "pizda", "pizda", "sr", "Profanity", "Exact", true, 4, seededAt },
                    { "pizde", "pizde", "sr", "Profanity", "Exact", true, 4, seededAt },
                    { "pizdu", "pizdu", "sr", "Profanity", "Exact", true, 4, seededAt },
                    { "govno", "govno", "sr", "Profanity", "Exact", true, 3, seededAt },
                    { "govna", "govna", "sr", "Profanity", "Exact", true, 3, seededAt },
                    { "sranje", "sranje", "sr", "Profanity", "Exact", true, 3, seededAt },
                    { "serem", "serem", "sr", "Profanity", "Exact", true, 3, seededAt },
                    { "seres", "seres", "sr", "Profanity", "Exact", true, 3, seededAt },
                    { "sere", "sere", "sr", "Profanity", "Exact", true, 3, seededAt },
                    { "usran", "usran", "sr", "Profanity", "Exact", true, 3, seededAt },
                    { "debil", "debil", "sr", "Harassment", "Exact", true, 2, seededAt },
                    { "debilu", "debilu", "sr", "Harassment", "Exact", true, 2, seededAt },
                    { "kreten", "kreten", "sr", "Harassment", "Exact", true, 2, seededAt },
                    { "kretenu", "kretenu", "sr", "Harassment", "Exact", true, 2, seededAt },
                    { "budala", "budala", "sr", "Harassment", "Exact", true, 2, seededAt },
                    { "budalo", "budalo", "sr", "Harassment", "Exact", true, 2, seededAt },
                    { "retard", "retard", "sr", "Slur", "Exact", true, 4, seededAt },
                    { "djubre", "djubre", "sr", "Harassment", "Exact", true, 3, seededAt },
                    { "djubrad", "djubrad", "sr", "Harassment", "Exact", true, 3, seededAt },
                    { "dzukela", "dzukela", "sr", "Harassment", "Exact", true, 3, seededAt },

                    { "jeb", "jeb", "sr", "Profanity", "Prefix", true, 4, seededAt },
                    { "kurc", "kurc", "sr", "Profanity", "Prefix", true, 4, seededAt },
                    { "pizd", "pizd", "sr", "Profanity", "Prefix", true, 4, seededAt },
                    { "govn", "govn", "sr", "Profanity", "Prefix", true, 3, seededAt },
                    { "sranj", "sranj", "sr", "Profanity", "Prefix", true, 3, seededAt },
                    { "debil", "debil", "sr", "Harassment", "Prefix", true, 2, seededAt },
                    { "kreten", "kreten", "sr", "Harassment", "Prefix", true, 2, seededAt },
                    { "retard", "retard", "sr", "Slur", "Prefix", true, 4, seededAt },
                    { "djubr", "djubr", "sr", "Harassment", "Prefix", true, 3, seededAt }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModerationTerms");
        }
    }
}
