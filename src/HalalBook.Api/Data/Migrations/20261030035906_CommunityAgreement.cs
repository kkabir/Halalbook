using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HalalBook.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CommunityAgreement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AcceptedTermsAt",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcceptedTermsVersion",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcceptedTermsAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AcceptedTermsVersion",
                table: "Users");
        }
    }
}
