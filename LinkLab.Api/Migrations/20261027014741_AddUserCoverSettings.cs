using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LinkLab.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUserCoverSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "CoverFocalPointX",
                table: "Users",
                type: "double precision",
                nullable: false,
                defaultValue: 50.0);

            migrationBuilder.AddColumn<double>(
                name: "CoverFocalPointY",
                table: "Users",
                type: "double precision",
                nullable: false,
                defaultValue: 50.0);

            migrationBuilder.AddColumn<string>(
                name: "CoverObjectKey",
                table: "Users",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "CoverZoom",
                table: "Users",
                type: "double precision",
                nullable: false,
                defaultValue: 1.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CoverFocalPointX",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CoverFocalPointY",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CoverObjectKey",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CoverZoom",
                table: "Users");
        }
    }
}
