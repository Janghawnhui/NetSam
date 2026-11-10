using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetSamLite.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddProhibitedSoftware : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProhibitedSoftware",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductName = table.Column<string>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    AddedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProhibitedSoftware", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProhibitedSoftware_ProductName",
                table: "ProhibitedSoftware",
                column: "ProductName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProhibitedSoftware");
        }
    }
}
