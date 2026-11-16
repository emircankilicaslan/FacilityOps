using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FacilityOps.Api.Data.MigrationsSqlServer
{
    /// <inheritdoc />
    public partial class Baslangic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DenetimKayitlari",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Zaman = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Kullanici = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Rol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Islem = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    VarlikTuru = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    VarlikId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Ayrinti = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DenetimKayitlari", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Istasyonlar",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Ad = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Sehir = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Istasyonlar", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Kullanicilar",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    KullaniciAdi = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SifreHash = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    SifreTuz = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    Rol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Aktif = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Kullanicilar", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Arizalar",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IstasyonId = table.Column<int>(type: "int", nullable: false),
                    Baslik = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Aciklama = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Oncelik = table.Column<int>(type: "int", nullable: false),
                    Durum = table.Column<int>(type: "int", nullable: false),
                    AtananKisi = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OlusturmaZamani = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SlaSonTarih = table.Column<DateTime>(type: "datetime2", nullable: false),
                    KapanmaZamani = table.Column<DateTime>(type: "datetime2", nullable: true)
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
                name: "YenilemeTokenleri",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    KullaniciId = table.Column<int>(type: "int", nullable: false),
                    TokenOzeti = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OlusturmaZamani = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SonKullanma = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IptalZamani = table.Column<DateTime>(type: "datetime2", nullable: true)
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

            migrationBuilder.CreateTable(
                name: "ArizaGecmisleri",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ArizaId = table.Column<int>(type: "int", nullable: false),
                    Islem = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EskiDurum = table.Column<int>(type: "int", nullable: true),
                    YeniDurum = table.Column<int>(type: "int", nullable: false),
                    Kullanici = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Zaman = table.Column<DateTime>(type: "datetime2", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_Kullanicilar_KullaniciAdi",
                table: "Kullanicilar",
                column: "KullaniciAdi",
                unique: true);

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
                name: "ArizaGecmisleri");

            migrationBuilder.DropTable(
                name: "DenetimKayitlari");

            migrationBuilder.DropTable(
                name: "YenilemeTokenleri");

            migrationBuilder.DropTable(
                name: "Arizalar");

            migrationBuilder.DropTable(
                name: "Kullanicilar");

            migrationBuilder.DropTable(
                name: "Istasyonlar");
        }
    }
}
