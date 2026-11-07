using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LinkLab.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRestorableGalleryItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Photos_GalleryId_SortOrder",
                table: "Photos");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "Photos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedThumbnailSettings",
                table: "Photos",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Photos_GalleryId_SortOrder",
                table: "Photos",
                columns: new[] { "GalleryId", "SortOrder" },
                unique: true,
                filter: "\"DeletedAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Photos_GalleryId_SortOrder",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "DeletedThumbnailSettings",
                table: "Photos");

            migrationBuilder.CreateIndex(
                name: "IX_Photos_GalleryId_SortOrder",
                table: "Photos",
                columns: new[] { "GalleryId", "SortOrder" },
                unique: true);
        }
    }
}
