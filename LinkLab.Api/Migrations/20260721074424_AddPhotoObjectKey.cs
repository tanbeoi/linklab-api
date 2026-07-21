using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LinkLab.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPhotoObjectKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "Photos");

            migrationBuilder.AddColumn<string>(
                name: "ObjectKey",
                table: "Photos",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Photos_ObjectKey",
                table: "Photos",
                column: "ObjectKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Photos_ObjectKey",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "ObjectKey",
                table: "Photos");

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "Photos",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
