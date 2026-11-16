using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;

namespace FacilityOps.Tests;

/// <summary>SQL Server yoksa testi atlar. <c>FACILITYOPS_TEST_SQL</c>: örn. "Server=(localdb)\mssqllocaldb;Integrated Security=true;TrustServerCertificate=True".</summary>
public sealed class SqlFactAttribute : FactAttribute
{
    public SqlFactAttribute()
    {
        if (!SqlServerTests.Available) Skip = "FACILITYOPS_TEST_SQL tanımlı değil; SQL Server gerektiren test atlandı.";
    }
}

/// <summary>
/// Uygulama SQL Server sağlayıcısıyla gerçek bir SQL Server'a karşı uçtan uca denenir: SQL Server migration'ları,
/// büyük/küçük harf duyarsız arama, kısıtlar, denetim kaydı ve dışa aktarma. Her sınıf kendi geçici veritabanını kurar ve siler.
/// </summary>
public sealed class SqlServerTests : IAsyncLifetime
{
    public static readonly string? ServerConnection = Environment.GetEnvironmentVariable("FACILITYOPS_TEST_SQL");
    public static bool Available => !string.IsNullOrWhiteSpace(ServerConnection);

    private readonly string _database = "FacilityOps_T_" + Guid.NewGuid().ToString("N")[..10];
    private WebApplicationFactory<Program>? _factory;
    private string _cs = "";

    public Task InitializeAsync()
    {
        if (!Available) return Task.CompletedTask;
        _cs = new SqlConnectionStringBuilder(ServerConnection) { InitialCatalog = _database }.ConnectionString;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Database:Provider", "SqlServer");
            b.UseSetting("ConnectionStrings:Default", _cs);
            b.UseSetting("Jwt:Key", "test-icin-en-az-otuz-iki-karakterlik-anahtar-0123456789");
            b.UseSetting("RateLimit:GirisDakika", "1000");
        });
        _ = _factory.Server; // uygulamayı başlatır, migration'lar uygulanır
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (!Available) return;
        SqlConnection.ClearAllPools();
        await using var conn = new SqlConnection(ServerConnection);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"IF DB_ID(N'{_database}') IS NOT NULL BEGIN ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}]; END";
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<int> Say(string sorgu)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sorgu;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private record Oturum(string Token, string YenilemeTokeni);

    private async Task<HttpClient> GirisliIstemci(string ad)
    {
        var c = _factory!.CreateClient();
        await c.PostAsJsonAsync("/auth/kayit", new { kullaniciAdi = ad, sifre = "sifre12345" });
        var r = await c.PostAsJsonAsync("/auth/giris", new { kullaniciAdi = ad, sifre = "sifre12345" });
        r.EnsureSuccessStatusCode();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await r.Content.ReadFromJsonAsync<Oturum>())!.Token);
        return c;
    }

    [SqlFact]
    public async Task MigrationlarSqlServerdaUygulanir_VeKisitlarVardir()
    {
        Assert.Equal(1, await Say("SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId LIKE '%Baslangic'"));
        foreach (var tablo in new[] { "Istasyonlar", "Arizalar", "ArizaGecmisleri", "Kullanicilar", "YenilemeTokenleri", "DenetimKayitlari" })
            Assert.Equal(1, await Say($"SELECT COUNT(*) FROM sys.tables WHERE name = '{tablo}'"));
        Assert.Equal(1, await Say("SELECT COUNT(*) FROM sys.indexes WHERE name = 'IX_Kullanicilar_KullaniciAdi' AND is_unique = 1"));
        Assert.Equal(1, await Say("SELECT COUNT(*) FROM sys.indexes WHERE name = 'IX_YenilemeTokenleri_TokenOzeti' AND is_unique = 1"));
        Assert.Equal(2, await Say("SELECT COUNT(*) FROM sys.foreign_keys WHERE name IN ('FK_Arizalar_Istasyonlar_IstasyonId', 'FK_ArizaGecmisleri_Arizalar_ArizaId')"));
        // Kısıtlı yabancı anahtar: arızası olan istasyon silinemez.
        Assert.Equal(1, await Say("SELECT COUNT(*) FROM sys.foreign_keys WHERE name = 'FK_Arizalar_Istasyonlar_IstasyonId' AND delete_referential_action = 0"));
    }

    [SqlFact]
    public async Task UctanUcaAkis_SqlServerdaCalisir()
    {
        var c = await GirisliIstemci("yonetici"); // ilk kayıt Yönetici olur
        var ist = await c.PostAsJsonAsync("/istasyonlar", new { ad = "Esenboğa", sehir = "Ankara" });
        Assert.Equal(HttpStatusCode.Created, ist.StatusCode);

        var ar = await c.PostAsJsonAsync("/arizalar", new { istasyonId = 1, baslik = "Pist Işıkları Arızası", aciklama = "Sönük", oncelik = 3 });
        Assert.Equal(HttpStatusCode.Created, ar.StatusCode);
        await c.PostAsJsonAsync("/arizalar", new { istasyonId = 1, baslik = "Bagaj bandı", oncelik = 1 });

        // Arama büyük/küçük harf duyarsız ve Türkçe karakterle çalışır (SQL Server harmanlaması).
        var liste = await c.GetFromJsonAsync<JsonElement>("/arizalar?ara=pist");
        Assert.Equal(1, liste.GetProperty("toplam").GetInt32());
        Assert.Equal(2, (await c.GetFromJsonAsync<JsonElement>("/arizalar")).GetProperty("toplam").GetInt32());
        Assert.Equal(1, (await c.GetFromJsonAsync<JsonElement>("/arizalar?oncelik=3")).GetProperty("toplam").GetInt32());

        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/arizalar/1/ata", new { kisi = "Ayşe" })).StatusCode);
        Assert.Equal(1, (await c.GetFromJsonAsync<JsonElement>("/arizalar?atanan=AYŞE")).GetProperty("toplam").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync("/arizalar/1/coz", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync("/arizalar/1/kapat", null)).StatusCode);

        var gecmis = await c.GetFromJsonAsync<JsonElement>("/arizalar/1/gecmis");
        Assert.Equal(4, gecmis.GetArrayLength()); // oluşturma, atama, çözme, kapatma

        var ozet = await c.GetFromJsonAsync<JsonElement>("/arizalar/ozet");
        Assert.Equal(1, ozet.GetProperty("durumlar").GetProperty("Kapandi").GetInt32());

        // Geçersiz durum geçişi ve istasyon silme kısıtı veritabanından bağımsız olarak 409 döner.
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync("/arizalar/1/kapat", null)).StatusCode);

        // Aynı kullanıcı adı ikinci kez kaydedilemez.
        Assert.Equal(HttpStatusCode.Conflict, (await _factory!.CreateClient().PostAsJsonAsync("/auth/kayit", new { kullaniciAdi = "yonetici", sifre = "sifre12345" })).StatusCode);

        // Denetim kaydı ve dışa aktarma SQL Server'da da çalışır.
        var denetim = await c.GetFromJsonAsync<JsonElement>("/denetim?islem=IstasyonOlusturuldu");
        Assert.Equal(1, denetim.GetProperty("toplam").GetInt32());
        var csv = await c.GetAsync("/arizalar/disa-aktar");
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        var xlsx = await c.GetAsync("/arizalar/disa-aktar.xlsx");
        Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
        using var kitap = new XLWorkbook(new MemoryStream(await xlsx.Content.ReadAsByteArrayAsync()));
        Assert.Equal("Başlık", kitap.Worksheet(1).Cell(1, 3).GetString());
    }

    [SqlFact]
    public async Task ArizasiOlanIstasyonVeritabaniDuzeyindeSilinemez()
    {
        var c = await GirisliIstemci("yonetici2");
        await c.PostAsJsonAsync("/istasyonlar", new { ad = "Silinmez", sehir = "Ankara" });
        var id = await Say("SELECT MAX(Id) FROM Istasyonlar");
        await c.PostAsJsonAsync("/arizalar", new { istasyonId = id, baslik = "Kayıt", oncelik = 1 });

        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM Istasyonlar WHERE Id = {id}";
        await Assert.ThrowsAsync<SqlException>(() => cmd.ExecuteNonQueryAsync());
    }
}
