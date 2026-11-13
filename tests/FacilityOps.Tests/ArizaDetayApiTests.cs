using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FacilityOps.Tests;

public class ArizaDetayApiTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly string _dbDosya = Path.Combine(Path.GetTempPath(), $"facilityops-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> _factory;

    public ArizaDetayApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Default", $"Data Source={_dbDosya}");
            b.UseSetting("Jwt:Key", "test-icin-en-az-otuz-iki-karakterlik-anahtar-0123456789");
        });
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbDosya); } catch { /* geçici dosya */ }
    }

    private async Task<HttpClient> GirisliIstemci(string ad, string sifre = "sifre12345")
    {
        var c = _factory.CreateClient();
        await c.PostAsJsonAsync("/auth/kayit", new { kullaniciAdi = ad, sifre });
        var r = await c.PostAsJsonAsync("/auth/giris", new { kullaniciAdi = ad, sifre });
        var token = (await r.Content.ReadFromJsonAsync<TokenYaniti>())!.Token;
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private record TokenYaniti(string Token);

    private static async Task Hazirla(HttpClient c)
    {
        await c.PostAsJsonAsync("/istasyonlar", new { ad = "Ankara Garı", sehir = "Ankara" });
        await c.PostAsJsonAsync("/arizalar", new { istasyonId = 1, baslik = "Sinyal", oncelik = 2 });
        await c.PostAsJsonAsync("/arizalar", new { istasyonId = 1, baslik = "Radar", oncelik = 1 });
        await c.PostAsJsonAsync("/arizalar/1/ata", new { kisi = "ayse" });
    }

    [Fact]
    public async Task Detay_VarOlanKayit200_OlmayanKayit404Doner()
    {
        var c = await GirisliIstemci("mudur");
        await Hazirla(c);

        var r = await c.GetAsync("/arizalar/1");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Sinyal", j.GetProperty("baslik").GetString());
        Assert.False(j.GetProperty("gecikmis").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/arizalar/999")).StatusCode);
    }

    [Fact]
    public async Task Detay_TokenOlmadan401Doner()
    {
        var r = await _factory.CreateClient().GetAsync("/arizalar/1");

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Liste_AtananVeAraFiltreleriCalisir()
    {
        var c = await GirisliIstemci("mudur");
        await Hazirla(c);

        var atanan = await c.GetFromJsonAsync<JsonElement>("/arizalar?atanan=AYSE");
        Assert.Equal(1, atanan.GetProperty("toplam").GetInt32());
        Assert.Equal("Sinyal", atanan.GetProperty("ogeler")[0].GetProperty("baslik").GetString());

        var ara = await c.GetFromJsonAsync<JsonElement>("/arizalar?ara=rad");
        Assert.Equal(1, ara.GetProperty("toplam").GetInt32());
        Assert.Equal("Radar", ara.GetProperty("ogeler")[0].GetProperty("baslik").GetString());

        var hepsi = await c.GetFromJsonAsync<JsonElement>("/arizalar");
        Assert.Equal(2, hepsi.GetProperty("toplam").GetInt32());
    }
}
