using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HalalBook.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SharedPostsInChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SharedAuthorName",
                table: "Messages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SharedImageUrl",
                table: "Messages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SharedPostId",
                table: "Messages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SharedPreview",
                table: "Messages",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SharedAuthorName",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "SharedImageUrl",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "SharedPostId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "SharedPreview",
                table: "Messages");
        }
    }
}
