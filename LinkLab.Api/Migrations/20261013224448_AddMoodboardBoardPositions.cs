using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LinkLab.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMoodboardBoardPositions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "BoardX",
                table: "Photos",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "BoardY",
                table: "Photos",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BoardZIndex",
                table: "Photos",
                type: "integer",
                nullable: true);

            // Existing moodboard photos become an initial four-column board so
            // existing published galleries remain visible after the upgrade.
            migrationBuilder.Sql("""
                UPDATE "Photos" AS p
                SET "BoardX" = 120 + (p."SortOrder" % 4) * 300,
                    "BoardY" = 120 + (p."SortOrder" / 4) * 280,
                    "BoardZIndex" = p."SortOrder" + 1
                FROM "Galleries" AS g
                WHERE p."GalleryId" = g."Id"
                  AND g."Purpose" = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BoardX",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "BoardY",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "BoardZIndex",
                table: "Photos");
        }
    }
}
