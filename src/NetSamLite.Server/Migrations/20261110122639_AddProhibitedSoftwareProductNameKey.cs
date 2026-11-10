using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetSamLite.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddProhibitedSoftwareProductNameKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProhibitedSoftware_ProductName",
                table: "ProhibitedSoftware");

            migrationBuilder.AddColumn<string>(
                name: "ProductNameKey",
                table: "ProhibitedSoftware",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            // 기존 데이터의 키를 채운다. SQLite에 정규식 치환 함수가 없어 연속 공백 축약까지는
            // 못 하고 TRIM+UPPER만 적용한다 — ProhibitedSoftwareEntity.ComputeKey(C#)와 완전히
            // 같지는 않지만, 새로 들어오는 행은 전부 ComputeKey를 거치므로 과거 데이터에만 있는
            // 차이다. 마이그레이션 전 기존 데이터 확인 결과 중복 없음(2026-11-10 기준).
            migrationBuilder.Sql(
                "UPDATE \"ProhibitedSoftware\" SET \"ProductNameKey\" = UPPER(TRIM(\"ProductName\"));");

            migrationBuilder.CreateIndex(
                name: "IX_ProhibitedSoftware_ProductNameKey",
                table: "ProhibitedSoftware",
                column: "ProductNameKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProhibitedSoftware_ProductNameKey",
                table: "ProhibitedSoftware");

            migrationBuilder.DropColumn(
                name: "ProductNameKey",
                table: "ProhibitedSoftware");

            migrationBuilder.CreateIndex(
                name: "IX_ProhibitedSoftware_ProductName",
                table: "ProhibitedSoftware",
                column: "ProductName",
                unique: true);
        }
    }
}
