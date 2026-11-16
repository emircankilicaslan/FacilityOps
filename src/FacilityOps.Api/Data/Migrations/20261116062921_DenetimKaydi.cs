using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FacilityOps.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class DenetimKaydi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DenetimKayitlari",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Zaman = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Kullanici = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Rol = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Islem = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    VarlikTuru = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    VarlikId = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    Ayrinti = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DenetimKayitlari", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DenetimKayitlari_Islem",
                table: "DenetimKayitlari",
                column: "Islem");

            migrationBuilder.CreateIndex(
                name: "IX_DenetimKayitlari_Kullanici",
                table: "DenetimKayitlari",
                column: "Kullanici");

            migrationBuilder.CreateIndex(
                name: "IX_DenetimKayitlari_Zaman",
                table: "DenetimKayitlari",
                column: "Zaman");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DenetimKayitlari");
        }
    }
}
