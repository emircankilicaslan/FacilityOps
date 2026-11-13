using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FacilityOps.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class YenilemeTokeniVeKullaniciDurumu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Aktif",
                table: "Kullanicilar",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "YenilemeTokenleri",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    KullaniciId = table.Column<int>(type: "INTEGER", nullable: false),
                    TokenOzeti = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OlusturmaZamani = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SonKullanma = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IptalZamani = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YenilemeTokenleri", x => x.Id);
                    table.ForeignKey(
                        name: "FK_YenilemeTokenleri_Kullanicilar_KullaniciId",
                        column: x => x.KullaniciId,
                        principalTable: "Kullanicilar",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_YenilemeTokenleri_KullaniciId",
                table: "YenilemeTokenleri",
                column: "KullaniciId");

            migrationBuilder.CreateIndex(
                name: "IX_YenilemeTokenleri_TokenOzeti",
                table: "YenilemeTokenleri",
                column: "TokenOzeti",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "YenilemeTokenleri");

            migrationBuilder.DropColumn(
                name: "Aktif",
                table: "Kullanicilar");
        }
    }
}
