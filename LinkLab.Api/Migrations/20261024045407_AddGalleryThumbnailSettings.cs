using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LinkLab.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGalleryThumbnailSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "ThumbnailFocalPointX",
                table: "Galleries",
                type: "double precision",
                nullable: false,
                defaultValue: 50.0);

            migrationBuilder.AddColumn<double>(
                name: "ThumbnailFocalPointY",
                table: "Galleries",
                type: "double precision",
                nullable: false,
                defaultValue: 50.0);

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailObjectKey",
                table: "Galleries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ThumbnailPhotoId",
                table: "Galleries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ThumbnailZoom",
                table: "Galleries",
                type: "double precision",
                nullable: false,
                defaultValue: 1.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ThumbnailFocalPointX",
                table: "Galleries");

            migrationBuilder.DropColumn(
                name: "ThumbnailFocalPointY",
                table: "Galleries");

            migrationBuilder.DropColumn(
                name: "ThumbnailObjectKey",
                table: "Galleries");

            migrationBuilder.DropColumn(
                name: "ThumbnailPhotoId",
                table: "Galleries");

            migrationBuilder.DropColumn(
                name: "ThumbnailZoom",
                table: "Galleries");
        }
    }
}
