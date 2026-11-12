using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TrackDesk.Tests;

public class ApiTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly string _dbDosya = Path.Combine(Path.GetTempPath(), $"trackdesk-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> _factory;

    public ApiTests(WebApplicationFactory<Program> factory)
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

    [Fact]
    public async Task TokenOlmadan_KorunanUcNoktalar401Doner()
    {
        var c = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/arizalar")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/istasyonlar")).StatusCode);
    }

    [Fact]
    public async Task YanlisSifre_401Doner()
    {
        var c = _factory.CreateClient();
        await c.PostAsJsonAsync("/auth/kayit", new { kullaniciAdi = "ayse", sifre = "sifre12345" });
        var r = await c.PostAsJsonAsync("/auth/giris", new { kullaniciAdi = "ayse", sifre = "yanlis-sifre" });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task KisaSifre_400Doner()
    {
        var r = await _factory.CreateClient().PostAsJsonAsync("/auth/kayit", new { kullaniciAdi = "veli", sifre = "123" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task AyniKullaniciAdi_409Doner()
    {
        var c = _factory.CreateClient();
        await c.PostAsJsonAsync("/auth/kayit", new { kullaniciAdi = "ali", sifre = "sifre12345" });
        var r = await c.PostAsJsonAsync("/auth/kayit", new { kullaniciAdi = "ALI", sifre = "sifre12345" });
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
    }

    [Fact]
    public async Task UctanUca_IstasyonVeAriza_AkisCalisir()
    {
        var c = await GirisliIstemci("yonetici");
        var ist = await c.PostAsJsonAsync("/istasyonlar", new { ad = "Esenboğa", sehir = "Ankara" });
        Assert.Equal(HttpStatusCode.Created, ist.StatusCode);

        var ar = await c.PostAsJsonAsync("/arizalar",
            new { istasyonId = 1, baslik = "Pist ışıkları", aciklama = "Sönük", oncelik = 3 });
        Assert.Equal(HttpStatusCode.Created, ar.StatusCode);

        var kapat = await c.PostAsync("/arizalar/1/kapat", null);
        Assert.Equal(HttpStatusCode.Conflict, kapat.StatusCode); // çözülmeden kapanamaz
    }

    [Fact]
    public async Task Personel_KapatamazYonetici_Kapatir()
    {
        var yonetici = await GirisliIstemci("mudur");      // ilk kayıt => Yonetici
        var personel = await GirisliIstemci("teknisyen");  // sonraki => Personel

        await yonetici.PostAsJsonAsync("/istasyonlar", new { ad = "Ankara Garı", sehir = "Ankara" });
        await yonetici.PostAsJsonAsync("/arizalar", new { istasyonId = 1, baslik = "Sinyal", oncelik = 2 });
        await personel.PostAsJsonAsync("/arizalar/1/ata", new { kisi = "teknisyen" });
        await personel.PostAsync("/arizalar/1/coz", null);

        Assert.Equal(HttpStatusCode.Forbidden, (await personel.PostAsync("/arizalar/1/kapat", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await yonetici.PostAsync("/arizalar/1/kapat", null)).StatusCode);
    }

    [Fact]
    public async Task Gecmis_KullaniciAdiniTokendenAlir()
    {
        var c = await GirisliIstemci("mudur");
        await c.PostAsJsonAsync("/istasyonlar", new { ad = "Ankara Garı", sehir = "Ankara" });
        await c.PostAsJsonAsync("/arizalar", new { istasyonId = 1, baslik = "Sinyal", oncelik = 2 });
        await c.PostAsJsonAsync("/arizalar/1/ata", new { kisi = "ayse" });

        var gecmis = await c.GetFromJsonAsync<System.Text.Json.JsonElement>("/arizalar/1/gecmis");

        Assert.Equal(2, gecmis.GetArrayLength());
        Assert.Equal("mudur", gecmis[1].GetProperty("kullanici").GetString());
    }

    [Fact]
    public async Task Liste_SayfalamaVeOzetCalisir()
    {
        var c = await GirisliIstemci("mudur");
        await c.PostAsJsonAsync("/istasyonlar", new { ad = "Ankara Garı", sehir = "Ankara" });
        for (var i = 0; i < 5; i++)
            await c.PostAsJsonAsync("/arizalar", new { istasyonId = 1, baslik = $"A{i}", oncelik = 1 });

        var sayfa = await c.GetFromJsonAsync<System.Text.Json.JsonElement>("/arizalar?sayfa=2&boyut=2");
        Assert.Equal(5, sayfa.GetProperty("toplam").GetInt32());
        Assert.Equal(3, sayfa.GetProperty("toplamSayfa").GetInt32());
        Assert.Equal(2, sayfa.GetProperty("ogeler").GetArrayLength());

        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/arizalar?boyut=1000")).StatusCode);

        var ozet = await c.GetFromJsonAsync<System.Text.Json.JsonElement>("/arizalar/ozet");
        Assert.Equal(5, ozet.GetProperty("durumlar").GetProperty("Acik").GetInt32());
    }

    [Fact]
    public async Task CsvDisaAktar_SadeceYonetici()
    {
        var yonetici = await GirisliIstemci("mudur");
        var personel = await GirisliIstemci("teknisyen");

        Assert.Equal(HttpStatusCode.Forbidden, (await personel.GetAsync("/arizalar/disa-aktar")).StatusCode);

        var r = await yonetici.GetAsync("/arizalar/disa-aktar");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("text/csv", r.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Hatalar_ProblemDetailsFormatindaDoner()
    {
        var c = await GirisliIstemci("mudur");
        var r = await c.PostAsync("/arizalar/999/coz", null);

        Assert.Equal("application/problem+json", r.Content.Headers.ContentType!.MediaType);
        var p = await r.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(404, p.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Saglik_AcikUctur_VeGuvenlikBasliklariVardir()
    {
        var r = await _factory.CreateClient().GetAsync("/saglik");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", r.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task GirisDenemeleri_LimiteTakilinca429Doner()
    {
        var c = _factory.WithWebHostBuilder(b => b.UseSetting("RateLimit:GirisDakika", "3")).CreateClient();
        var kodlar = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
            kodlar.Add((await c.PostAsJsonAsync("/auth/giris", new { kullaniciAdi = "x", sifre = "yanlis" })).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, kodlar[^1]);
        Assert.Equal(3, kodlar.Count(k => k == HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task OlmayanKayit_404Doner()
    {
        var c = await GirisliIstemci("yonetici");
        var r = await c.PostAsync("/arizalar/999/coz", null);
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }
}
