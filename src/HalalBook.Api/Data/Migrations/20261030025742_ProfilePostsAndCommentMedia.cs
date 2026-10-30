using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HalalBook.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProfilePostsAndCommentMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WhoCanPost",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetUserId",
                table: "Posts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MediaUrl",
                table: "Comments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Posts_TargetUserId",
                table: "Posts",
                column: "TargetUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Posts_Users_TargetUserId",
                table: "Posts",
                column: "TargetUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Posts_Users_TargetUserId",
                table: "Posts");

            migrationBuilder.DropIndex(
                name: "IX_Posts_TargetUserId",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "WhoCanPost",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TargetUserId",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "MediaUrl",
                table: "Comments");
        }
    }
}
