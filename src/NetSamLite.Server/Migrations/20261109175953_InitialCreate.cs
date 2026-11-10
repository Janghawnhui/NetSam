using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetSamLite.Server.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    DeviceId = table.Column<string>(type: "TEXT", nullable: false),
                    ComputerName = table.Column<string>(type: "TEXT", nullable: false),
                    Manufacturer = table.Column<string>(type: "TEXT", nullable: true),
                    Model = table.Column<string>(type: "TEXT", nullable: true),
                    SerialNumber = table.Column<string>(type: "TEXT", nullable: true),
                    ProcessorName = table.Column<string>(type: "TEXT", nullable: true),
                    ProcessorCoreCount = table.Column<int>(type: "INTEGER", nullable: false),
                    OperatingSystemCaption = table.Column<string>(type: "TEXT", nullable: true),
                    OperatingSystemVersion = table.Column<string>(type: "TEXT", nullable: true),
                    OperatingSystemBuildNumber = table.Column<string>(type: "TEXT", nullable: true),
                    TotalPhysicalMemoryBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.DeviceId);
                });

            migrationBuilder.CreateTable(
                name: "InstalledSoftwareSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DeviceId = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayVersion = table.Column<string>(type: "TEXT", nullable: true),
                    Publisher = table.Column<string>(type: "TEXT", nullable: true),
                    InstallDate = table.Column<string>(type: "TEXT", nullable: true),
                    InstallLocation = table.Column<string>(type: "TEXT", nullable: true),
                    FirstSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstalledSoftwareSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SoftwareChangeEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DeviceId = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayVersion = table.Column<string>(type: "TEXT", nullable: true),
                    ChangeType = table.Column<int>(type: "INTEGER", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SoftwareChangeEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeviceDisks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DeviceId = table.Column<string>(type: "TEXT", nullable: false),
                    Model = table.Column<string>(type: "TEXT", nullable: true),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceDisks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceDisks_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "DeviceId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceDisks_DeviceId",
                table: "DeviceDisks",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_InstalledSoftwareSnapshots_DeviceId_DisplayName",
                table: "InstalledSoftwareSnapshots",
                columns: new[] { "DeviceId", "DisplayName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareChangeEvents_DeviceId",
                table: "SoftwareChangeEvents",
                column: "DeviceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceDisks");

            migrationBuilder.DropTable(
                name: "InstalledSoftwareSnapshots");

            migrationBuilder.DropTable(
                name: "SoftwareChangeEvents");

            migrationBuilder.DropTable(
                name: "Devices");
        }
    }
}
