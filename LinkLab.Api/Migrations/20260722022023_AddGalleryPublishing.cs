using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LinkLab.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGalleryPublishing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPublished",
                table: "Galleries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedAtUtc",
                table: "Galleries",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPublished",
                table: "Galleries");

            migrationBuilder.DropColumn(
                name: "PublishedAtUtc",
                table: "Galleries");
        }
    }
}
