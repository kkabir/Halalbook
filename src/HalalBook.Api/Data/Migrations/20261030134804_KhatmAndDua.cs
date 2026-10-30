using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HalalBook.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class KhatmAndDua : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Khatms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Intention = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Khatms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Khatms_Users_CreatorId",
                        column: x => x.CreatorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KhatmPart",
                columns: table => new
                {
                    KhatmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Juz = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClaimedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DoneAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KhatmPart", x => new { x.KhatmId, x.Juz });
                    table.ForeignKey(
                        name: "FK_KhatmPart_Khatms_KhatmId",
                        column: x => x.KhatmId,
                        principalTable: "Khatms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KhatmPart_UserId",
                table: "KhatmPart",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Khatms_CreatorId",
                table: "Khatms",
                column: "CreatorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KhatmPart");

            migrationBuilder.DropTable(
                name: "Khatms");
        }
    }
}
