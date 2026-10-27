using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LinkLab.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAvatarSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "AvatarFocalPointX",
                table: "Users",
                type: "double precision",
                nullable: false,
                defaultValue: 50.0);

            migrationBuilder.AddColumn<double>(
                name: "AvatarFocalPointY",
                table: "Users",
                type: "double precision",
                nullable: false,
                defaultValue: 50.0);

            migrationBuilder.AddColumn<string>(
                name: "AvatarObjectKey",
                table: "Users",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "AvatarZoom",
                table: "Users",
                type: "double precision",
                nullable: false,
                defaultValue: 1.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AvatarFocalPointX",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AvatarFocalPointY",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AvatarObjectKey",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AvatarZoom",
                table: "Users");
        }
    }
}
