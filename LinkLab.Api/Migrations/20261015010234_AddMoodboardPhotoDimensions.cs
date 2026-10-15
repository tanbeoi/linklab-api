using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LinkLab.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMoodboardPhotoDimensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "BoardWidth",
                table: "Photos",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "CaptionHeight",
                table: "Photos",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BoardWidth",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "CaptionHeight",
                table: "Photos");
        }
    }
}
