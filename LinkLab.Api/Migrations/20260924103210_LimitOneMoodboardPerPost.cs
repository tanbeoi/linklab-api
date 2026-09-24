using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LinkLab.Api.Migrations
{
    /// <inheritdoc />
    public partial class LimitOneMoodboardPerPost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Galleries_OneMoodboardPerPost",
                table: "Galleries",
                column: "CollabPostId",
                unique: true,
                filter: "\"Purpose\" = 1 AND \"CollabPostId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Galleries_OneMoodboardPerPost",
                table: "Galleries");
        }
    }
}
