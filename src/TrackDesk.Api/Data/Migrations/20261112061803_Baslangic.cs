using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrackDesk.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Baslangic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Istasyonlar",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Ad = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Sehir = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Istasyonlar", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Kullanicilar",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    KullaniciAdi = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    SifreHash = table.Column<byte[]>(type: "BLOB", nullable: false),
                    SifreTuz = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Rol = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Kullanicilar", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Arizalar",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IstasyonId = table.Column<int>(type: "INTEGER", nullable: false),
                    Baslik = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Aciklama = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Oncelik = table.Column<int>(type: "INTEGER", nullable: false),
                    Durum = table.Column<int>(type: "INTEGER", nullable: false),
                    AtananKisi = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    OlusturmaZamani = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SlaSonTarih = table.Column<DateTime>(type: "TEXT", nullable: false),
                    KapanmaZamani = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Arizalar", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Arizalar_Istasyonlar_IstasyonId",
                        column: x => x.IstasyonId,
                        principalTable: "Istasyonlar",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ArizaGecmisleri",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ArizaId = table.Column<int>(type: "INTEGER", nullable: false),
                    Islem = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    EskiDurum = table.Column<int>(type: "INTEGER", nullable: true),
                    YeniDurum = table.Column<int>(type: "INTEGER", nullable: false),
                    Kullanici = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Zaman = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArizaGecmisleri", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArizaGecmisleri_Arizalar_ArizaId",
                        column: x => x.ArizaId,
                        principalTable: "Arizalar",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArizaGecmisleri_ArizaId",
                table: "ArizaGecmisleri",
                column: "ArizaId");

            migrationBuilder.CreateIndex(
                name: "IX_Arizalar_Durum_Oncelik",
                table: "Arizalar",
                columns: new[] { "Durum", "Oncelik" });

            migrationBuilder.CreateIndex(
                name: "IX_Arizalar_IstasyonId",
                table: "Arizalar",
                column: "IstasyonId");

            migrationBuilder.CreateIndex(
                name: "IX_Arizalar_SlaSonTarih",
                table: "Arizalar",
                column: "SlaSonTarih");

            migrationBuilder.CreateIndex(
                name: "IX_Kullanicilar_KullaniciAdi",
                table: "Kullanicilar",
                column: "KullaniciAdi",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArizaGecmisleri");

            migrationBuilder.DropTable(
                name: "Kullanicilar");

            migrationBuilder.DropTable(
                name: "Arizalar");

            migrationBuilder.DropTable(
                name: "Istasyonlar");
        }
    }
}
