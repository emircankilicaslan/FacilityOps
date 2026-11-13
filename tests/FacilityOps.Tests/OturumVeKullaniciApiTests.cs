using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FacilityOps.Tests;

/// <summary>Yenileme tokenı, çıkış, şifre değiştirme ve yönetici kullanıcı uçları.</summary>
public class OturumVeKullaniciApiTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly string _dbDosya = Path.Combine(Path.GetTempPath(), $"facilityops-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> _factory;

    public OturumVeKullaniciApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Default", $"Data Source={_dbDosya}");
            b.UseSetting("Jwt:Key", "test-icin-en-az-otuz-iki-karakterlik-anahtar-0123456789");
            b.UseSetting("RateLimit:GirisDakika", "1000");
        });
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbDosya); } catch { /* geçici dosya */ }
    }

    private record Oturum(string Token, string YenilemeTokeni);

    private async Task<Oturum> GirisYap(HttpClient c, string ad, string sifre = "sifre12345")
    {
        await c.PostAsJsonAsync("/auth/kayit", new { kullaniciAdi = ad, sifre });
        var r = await c.PostAsJsonAsync("/auth/giris", new { kullaniciAdi = ad, sifre });
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<Oturum>())!;
    }

    private static HttpClient Yetkili(HttpClient c, string token)
    {
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private record Kullanici(int Id, string KullaniciAdi, string Rol, bool Aktif);

    [Fact]
    public async Task Giris_YenilemeTokeniDoner_VeYenileYeniCiftVerir()
    {
        var c = _factory.CreateClient();
        var ilk = await GirisYap(c, "ayse");
        Assert.False(string.IsNullOrWhiteSpace(ilk.YenilemeTokeni));

        var r = await c.PostAsJsonAsync("/auth/yenile", new { yenilemeTokeni = ilk.YenilemeTokeni });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var yeni = (await r.Content.ReadFromJsonAsync<Oturum>())!;

        Assert.NotEqual(ilk.YenilemeTokeni, yeni.YenilemeTokeni);
        var liste = await Yetkili(_factory.CreateClient(), yeni.Token).GetAsync("/istasyonlar");
        Assert.Equal(HttpStatusCode.OK, liste.StatusCode);
    }

    [Fact]
    public async Task EskiYenilemeTokeniTekrarKullanilirsa_TumOturumlarKapanir()
    {
        var c = _factory.CreateClient();
        var ilk = await GirisYap(c, "ayse");

        var ikinci = (await (await c.PostAsJsonAsync("/auth/yenile", new { yenilemeTokeni = ilk.YenilemeTokeni }))
            .Content.ReadFromJsonAsync<Oturum>())!;

        // Eski token ikinci kez sunulur: reddedilir ve tekrar kullanım nedeniyle yeni token da iptal edilir.
        var tekrar = await c.PostAsJsonAsync("/auth/yenile", new { yenilemeTokeni = ilk.YenilemeTokeni });
        Assert.Equal(HttpStatusCode.Unauthorized, tekrar.StatusCode);

        var yeniArtikGecersiz = await c.PostAsJsonAsync("/auth/yenile", new { yenilemeTokeni = ikinci.YenilemeTokeni });
        Assert.Equal(HttpStatusCode.Unauthorized, yeniArtikGecersiz.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("uydurma-token")]
    public async Task GecersizYenilemeTokeni_401Doner(string? token)
    {
        var r = await _factory.CreateClient().PostAsJsonAsync("/auth/yenile", new { yenilemeTokeni = token });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Cikis_YenilemeTokenInIptalEder()
    {
        var c = _factory.CreateClient();
        var o = await GirisYap(c, "ayse");

        var cikis = await c.PostAsJsonAsync("/auth/cikis", new { yenilemeTokeni = o.YenilemeTokeni });
        Assert.Equal(HttpStatusCode.NoContent, cikis.StatusCode);

        var yenile = await c.PostAsJsonAsync("/auth/yenile", new { yenilemeTokeni = o.YenilemeTokeni });
        Assert.Equal(HttpStatusCode.Unauthorized, yenile.StatusCode);

        // Bilinmeyen token ile çıkış hata vermez
        var yine = await c.PostAsJsonAsync("/auth/cikis", new { yenilemeTokeni = "yok" });
        Assert.Equal(HttpStatusCode.NoContent, yine.StatusCode);
    }

    [Fact]
    public async Task SifreDegistir_EskiSifreyiGecersizKilar_VeOturumlariKapatir()
    {
        var c = _factory.CreateClient();
        var o = await GirisYap(c, "ayse");
        var yetkili = Yetkili(_factory.CreateClient(), o.Token);

        var yanlis = await yetkili.PostAsJsonAsync("/auth/sifre-degistir", new { mevcutSifre = "yanlis", yeniSifre = "yeni-sifre-123" });
        Assert.Equal(HttpStatusCode.BadRequest, yanlis.StatusCode);

        var kisa = await yetkili.PostAsJsonAsync("/auth/sifre-degistir", new { mevcutSifre = "sifre12345", yeniSifre = "kisa" });
        Assert.Equal(HttpStatusCode.BadRequest, kisa.StatusCode);

        var ok = await yetkili.PostAsJsonAsync("/auth/sifre-degistir", new { mevcutSifre = "sifre12345", yeniSifre = "yeni-sifre-123" });
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await c.PostAsJsonAsync("/auth/yenile", new { yenilemeTokeni = o.YenilemeTokeni })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await c.PostAsJsonAsync("/auth/giris", new { kullaniciAdi = "ayse", sifre = "sifre12345" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await c.PostAsJsonAsync("/auth/giris", new { kullaniciAdi = "ayse", sifre = "yeni-sifre-123" })).StatusCode);
    }

    [Fact]
    public async Task SifreDegistir_TokensizIstek401Doner()
    {
        var r = await _factory.CreateClient().PostAsJsonAsync("/auth/sifre-degistir", new { mevcutSifre = "a", yeniSifre = "b" });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task KullaniciListesi_YalnizcaYoneticiGorur()
    {
        var c = _factory.CreateClient();
        var yonetici = await GirisYap(c, "yonetici");   // ilk kayıt → Yönetici
        var personel = await GirisYap(c, "personel");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await Yetkili(_factory.CreateClient(), personel.Token).GetAsync("/kullanicilar")).StatusCode);

        var r = await Yetkili(_factory.CreateClient(), yonetici.Token).GetAsync("/kullanicilar");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var liste = (await r.Content.ReadFromJsonAsync<List<Kullanici>>())!;
        Assert.Equal(["personel", "yonetici"], liste.Select(k => k.KullaniciAdi).ToArray());
        Assert.DoesNotContain("sifre", await r.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RolDegistir_PersoneliYoneticiYapar_GecersizRol400()
    {
        var c = _factory.CreateClient();
        var yonetici = await GirisYap(c, "yonetici");
        await GirisYap(c, "personel");
        var y = Yetkili(_factory.CreateClient(), yonetici.Token);
        var personelId = (await y.GetFromJsonAsync<List<Kullanici>>("/kullanicilar"))!.Single(k => k.KullaniciAdi == "personel").Id;

        var gecersiz = await y.PutAsJsonAsync($"/kullanicilar/{personelId}/rol", new { rol = "Patron" });
        Assert.Equal(HttpStatusCode.BadRequest, gecersiz.StatusCode);

        var ok = await y.PutAsJsonAsync($"/kullanicilar/{personelId}/rol", new { rol = "Yonetici" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("Yonetici", (await ok.Content.ReadFromJsonAsync<Kullanici>())!.Rol);

        Assert.Equal(HttpStatusCode.NotFound,
            (await y.PutAsJsonAsync("/kullanicilar/9999/rol", new { rol = "Personel" })).StatusCode);
    }

    [Fact]
    public async Task SonAktifYonetici_PasifeAlinamaz_RoluDusurulemez()
    {
        var c = _factory.CreateClient();
        var yonetici = await GirisYap(c, "yonetici");
        var y = Yetkili(_factory.CreateClient(), yonetici.Token);
        var id = (await y.GetFromJsonAsync<List<Kullanici>>("/kullanicilar"))!.Single().Id;

        Assert.Equal(HttpStatusCode.Conflict,
            (await y.PutAsJsonAsync($"/kullanicilar/{id}/durum", new { aktif = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await y.PutAsJsonAsync($"/kullanicilar/{id}/rol", new { rol = "Personel" })).StatusCode);
    }

    [Fact]
    public async Task PasifKullanici_GirisYapamaz_VeTokenYenileyemez()
    {
        var c = _factory.CreateClient();
        var yonetici = await GirisYap(c, "yonetici");
        var personel = await GirisYap(c, "personel");
        var y = Yetkili(_factory.CreateClient(), yonetici.Token);
        var personelId = (await y.GetFromJsonAsync<List<Kullanici>>("/kullanicilar"))!.Single(k => k.KullaniciAdi == "personel").Id;

        var pasif = await y.PutAsJsonAsync($"/kullanicilar/{personelId}/durum", new { aktif = false });
        Assert.Equal(HttpStatusCode.OK, pasif.StatusCode);
        Assert.False((await pasif.Content.ReadFromJsonAsync<Kullanici>())!.Aktif);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await c.PostAsJsonAsync("/auth/giris", new { kullaniciAdi = "personel", sifre = "sifre12345" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await c.PostAsJsonAsync("/auth/yenile", new { yenilemeTokeni = personel.YenilemeTokeni })).StatusCode);

        // Tekrar aktifleştirilince giriş yeniden mümkün
        await y.PutAsJsonAsync($"/kullanicilar/{personelId}/durum", new { aktif = true });
        Assert.Equal(HttpStatusCode.OK,
            (await c.PostAsJsonAsync("/auth/giris", new { kullaniciAdi = "personel", sifre = "sifre12345" })).StatusCode);
    }
}
