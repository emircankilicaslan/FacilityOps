using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FacilityOps.Tests;

/// <summary>Veritabanı sağlayıcısı ayarının doğrulanması ve bozuk istek gövdesinin 400 ile reddedilmesi.</summary>
public class SaglayiciVeIstekHatasiTests : IDisposable
{
    private readonly string _dbDosya = Path.Combine(Path.GetTempPath(), $"facilityops-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbDosya); } catch { /* geçici dosya */ }
    }

    private WebApplicationFactory<Program> Fabrika(params (string Ad, string Deger)[] ayarlar) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Jwt:Key", "test-icin-en-az-otuz-iki-karakterlik-anahtar-0123456789");
            b.UseSetting("RateLimit:GirisDakika", "1000");
            foreach (var (ad, deger) in ayarlar) b.UseSetting(ad, deger);
        });

    [Fact]
    public void BilinmeyenSaglayici_UygulamaBaslamaz()
    {
        using var f = Fabrika(("Database:Provider", "Oracle"), ("ConnectionStrings:Default", $"Data Source={_dbDosya}"));
        var hata = Assert.Throws<InvalidOperationException>(() => f.CreateClient());
        Assert.Contains("Database:Provider", hata.Message);
    }

    [Fact]
    public void SqlServerBaglantiDizesiOlmadan_UygulamaBaslamaz()
    {
        using var f = Fabrika(("Database:Provider", "SqlServer"));
        var hata = Assert.Throws<InvalidOperationException>(() => f.CreateClient());
        Assert.Contains("ConnectionStrings", hata.Message);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("Sqlite")]
    public async Task SqliteSaglayici_HarfDuyarsizSecilirVeCalisir(string saglayici)
    {
        using var f = Fabrika(("Database:Provider", saglayici), ("ConnectionStrings:Default", $"Data Source={_dbDosya}"));
        var r = await f.CreateClient().GetAsync("/saglik");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Theory]
    [InlineData("{\"istasyonId\":1,\"baslik\":\"x\",\"oncelik\":\"Kritik\"}")]   // sayı beklenen enum'a metin
    [InlineData("{\"istasyonId\":\"bir\",\"baslik\":\"x\",\"oncelik\":1}")]       // sayı beklenen alana metin
    [InlineData("{bozuk json")]
    public async Task BozukIstekGovdesi_500DegilDe400Doner_AyrintiSizdirmaz(string govde)
    {
        using var f = Fabrika(("ConnectionStrings:Default", $"Data Source={_dbDosya}"));
        var c = f.CreateClient();
        await c.PostAsJsonAsync("/auth/kayit", new { kullaniciAdi = "yonetici", sifre = "sifre12345" });
        var giris = await c.PostAsJsonAsync("/auth/giris", new { kullaniciAdi = "yonetici", sifre = "sifre12345" });
        var token = (await giris.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("token").GetString();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var r = await c.PostAsync("/arizalar", new StringContent(govde, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var ham = await r.Content.ReadAsStringAsync();
        Assert.Contains("Geçersiz istek", ham);
        Assert.DoesNotContain("System.", ham); // yığın/tür bilgisi sızmaz
    }
}
