using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HalalBook.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PrivacySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WhoCanFindMe",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WhoCanFollow",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WhoCanMessage",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WhoCanSeeFriendsList",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WhoCanSendFriendRequests",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WhoCanFindMe",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "WhoCanFollow",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "WhoCanMessage",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "WhoCanSeeFriendsList",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "WhoCanSendFriendRequests",
                table: "Users");
        }
    }
}
