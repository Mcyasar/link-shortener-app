using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LinkShortener.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLinkClickOutboxTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LinkClickOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShortCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ClickedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LinkClickOutbox", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LinkClickOutbox_PendingStatus",
                table: "LinkClickOutbox",
                column: "Status",
                filter: "\"Status\" = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LinkClickOutbox");
        }
    }
}
