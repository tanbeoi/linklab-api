using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LinkLab.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGalleriesSortOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Galleries_CreatedAtUtc",
                table: "Galleries");

            migrationBuilder.DropIndex(
                name: "IX_Galleries_OwnerId",
                table: "Galleries");

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "Galleries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Galleries_OwnerId_SortOrder",
                table: "Galleries",
                columns: new[] { "OwnerId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Galleries_OwnerId_SortOrder",
                table: "Galleries");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "Galleries");

            migrationBuilder.CreateIndex(
                name: "IX_Galleries_CreatedAtUtc",
                table: "Galleries",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Galleries_OwnerId",
                table: "Galleries",
                column: "OwnerId");
        }
    }
}
