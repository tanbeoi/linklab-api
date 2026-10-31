using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LinkLab.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPortfolioYoutubeVideos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "VideoAspectRatio",
                table: "Photos",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "YoutubeVideoId",
                table: "Photos",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VideoAspectRatio",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "YoutubeVideoId",
                table: "Photos");
        }
    }
}
