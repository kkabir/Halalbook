using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HalalBook.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class FriendLists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FriendListId",
                table: "Posts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FriendLists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Icon = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FriendLists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FriendListMembers",
                columns: table => new
                {
                    ListId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FriendListMembers", x => new { x.ListId, x.UserId });
                    table.ForeignKey(
                        name: "FK_FriendListMembers_FriendLists_ListId",
                        column: x => x.ListId,
                        principalTable: "FriendLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FriendListMembers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FriendListMembers_UserId",
                table: "FriendListMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_FriendLists_OwnerId",
                table: "FriendLists",
                column: "OwnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FriendListMembers");

            migrationBuilder.DropTable(
                name: "FriendLists");

            migrationBuilder.DropColumn(
                name: "FriendListId",
                table: "Posts");
        }
    }
}
