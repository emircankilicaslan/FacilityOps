using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using FacilityOps.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FacilityOps.Tests;

/// <summary>Genel denetim kaydı (giriş, kullanıcı yönetimi, istasyon) ve Excel çıktıları.</summary>
public class DenetimKaydiTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly string _dbDosya = Path.Combine(Path.GetTempPath(), $"facilityops-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> _factory;

    public DenetimKaydiTests(WebApplicationFactory<Program> factory)
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

    /// <summary>Her çağrı kendi istemcisini döndürür; ilk kayıt olan Yönetici olduğundan yönetici ilk oluşturulur.</summary>
    private async Task<(HttpClient Yonetici, HttpClient Personel)> IkiKullanici()
    {
        var y = Yetkili(_factory.CreateClient(), (await GirisYap(_factory.CreateClient(), "yonetici")).Token);
        var p = Yetkili(_factory.CreateClient(), (await GirisYap(_factory.CreateClient(), "personel")).Token);
        return (y, p);
    }

    private static async Task<JsonElement> Liste(HttpClient yonetici, string sorgu = "")
    {
        var r = await yonetici.GetAsync($"/denetim{sorgu}");
        r.EnsureSuccessStatusCode();
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static List<JsonElement> Ogeler(JsonElement liste) => liste.GetProperty("ogeler").EnumerateArray().ToList();

    [Fact]
    public async Task KayitGirisVeBasarisizGiris_KaydedilirSifreKayidaGirmez()
    {
        var (yonetici, _) = await IkiKullanici();
        const string yanlis = "cok-gizli-yanlis-sifre-9";
        var r = await _factory.CreateClient().PostAsJsonAsync("/auth/giris", new { kullaniciAdi = "yonetici", sifre = yanlis });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);

        var ham = await yonetici.GetStringAsync("/denetim?boyut=100");
        Assert.DoesNotContain(yanlis, ham);
        Assert.DoesNotContain("sifre12345", ham);

        var islemler = Ogeler(JsonDocument.Parse(ham).RootElement).Select(o => o.GetProperty("islem").GetString()).ToList();
        Assert.Contains("KullaniciKaydi", islemler);
        Assert.Contains("Giris", islemler);
        Assert.Contains("GirisBasarisiz", islemler);

        var basarisiz = Ogeler(await Liste(yonetici, "?islem=GirisBasarisiz")).Single();
        Assert.Equal("yonetici", basarisiz.GetProperty("kullanici").GetString());
        Assert.True(DateTime.SpecifyKind(basarisiz.GetProperty("zaman").GetDateTime(), DateTimeKind.Utc) > DateTime.UtcNow.AddMinutes(-5)); // sunucu zamanı UTC, Z eki olmadan döner
    }

    [Fact]
    public async Task RolDurumVeIstasyonIslemleri_YapanKullaniciylaKaydedilir()
    {
        var (yonetici, personel) = await IkiKullanici();
        var kullanicilar = (await yonetici.GetFromJsonAsync<JsonElement>("/kullanicilar")).EnumerateArray().ToList();
        var personelId = kullanicilar.Single(k => k.GetProperty("kullaniciAdi").GetString() == "personel").GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.OK, (await yonetici.PutAsJsonAsync($"/kullanicilar/{personelId}/rol", new { rol = "Yonetici" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await yonetici.PutAsJsonAsync($"/kullanicilar/{personelId}/durum", new { aktif = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await yonetici.PostAsJsonAsync("/istasyonlar", new { ad = "Ankara Gar", sehir = "Ankara" })).StatusCode);

        var rol = Ogeler(await Liste(yonetici, "?islem=RolDegisti")).Single();
        Assert.Equal("yonetici", rol.GetProperty("kullanici").GetString());
        Assert.Equal(personelId.ToString(), rol.GetProperty("varlikId").GetString());
        Assert.Contains("Personel → Yonetici", rol.GetProperty("ayrinti").GetString());

        Assert.Single(Ogeler(await Liste(yonetici, "?islem=KullaniciPasifeAlindi")));
        var ist = Ogeler(await Liste(yonetici, "?islem=IstasyonOlusturuldu")).Single();
        Assert.Equal("Ankara Gar · Ankara", ist.GetProperty("ayrinti").GetString());
        Assert.Equal("Yonetici", ist.GetProperty("rol").GetString());
        _ = personel;
    }

    [Fact]
    public async Task SifreDegistirme_KaydedilirYeniSifreGorunmez()
    {
        var (yonetici, personel) = await IkiKullanici();
        var r = await personel.PostAsJsonAsync("/auth/sifre-degistir", new { mevcutSifre = "sifre12345", yeniSifre = "yepyeni-sifre-77" });
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);

        var ham = await yonetici.GetStringAsync("/denetim?islem=SifreDegisti");
        Assert.DoesNotContain("yepyeni-sifre-77", ham);
        var kayit = Ogeler(JsonDocument.Parse(ham).RootElement).Single();
        Assert.Equal("personel", kayit.GetProperty("kullanici").GetString());
    }

    [Fact]
    public async Task IptalEdilmisTokenTekrarKullanilirsa_GuvenlikOlayiKaydedilir()
    {
        var (yonetici, _) = await IkiKullanici();
        var c = _factory.CreateClient();
        var oturum = await GirisYap(c, "ayse");
        var ilk = await c.PostAsJsonAsync("/auth/yenile", new { yenilemeTokeni = oturum.YenilemeTokeni });
        Assert.Equal(HttpStatusCode.OK, ilk.StatusCode);

        var tekrar = await c.PostAsJsonAsync("/auth/yenile", new { yenilemeTokeni = oturum.YenilemeTokeni });
        Assert.Equal(HttpStatusCode.Unauthorized, tekrar.StatusCode);

        var olay = Ogeler(await Liste(yonetici, "?islem=TokenTekrarKullanimi")).Single();
        Assert.Equal("ayse", olay.GetProperty("kullanici").GetString());
        Assert.DoesNotContain(oturum.YenilemeTokeni, olay.ToString());
    }

    [Fact]
    public async Task DenetimUclari_SadeceYonetici()
    {
        var (yonetici, personel) = await IkiKullanici();
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/denetim")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await personel.GetAsync("/denetim")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await personel.GetAsync("/denetim/disa-aktar.xlsx")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await personel.GetAsync("/arizalar/disa-aktar.xlsx")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await yonetici.GetAsync("/denetim")).StatusCode);
    }

    [Fact]
    public async Task FiltreSayfalamaVeDogrulama()
    {
        var (yonetici, _) = await IkiKullanici();
        Assert.Equal(HttpStatusCode.BadRequest, (await yonetici.GetAsync("/denetim?sayfa=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await yonetici.GetAsync("/denetim?boyut=101")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await yonetici.GetAsync("/denetim?baslangic=2026-02-01&bitis=2026-01-01")).StatusCode);

        Assert.Equal(0, (await Liste(yonetici, "?kullanici=yok-boyle-biri")).GetProperty("toplam").GetInt32());
        Assert.Equal(0, (await Liste(yonetici, $"?baslangic={DateTime.UtcNow.AddDays(1):yyyy-MM-dd}")).GetProperty("toplam").GetInt32());

        var tek = await Liste(yonetici, "?boyut=1");
        Assert.Single(Ogeler(tek));
        Assert.True(tek.GetProperty("toplam").GetInt32() >= 2);
        Assert.True(tek.GetProperty("toplamSayfa").GetInt32() >= 2);
    }

    [Fact]
    public async Task ExcelCiktisi_DenetimVeArizalar_FormulEnjeksiyonuCalismaz()
    {
        var (yonetici, _) = await IkiKullanici();
        var ist = await yonetici.PostAsJsonAsync("/istasyonlar", new { ad = "=HYPERLINK(\"http://kotu.example\",\"tikla\")", sehir = "Ankara" });
        var istId = (await ist.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var ariza = await yonetici.PostAsJsonAsync("/arizalar", new { istasyonId = istId, baslik = "=1+1", aciklama = "x", oncelik = 3 });
        Assert.Equal(HttpStatusCode.Created, ariza.StatusCode);

        var arizalar = await yonetici.GetAsync("/arizalar/disa-aktar.xlsx");
        Assert.Equal(HttpStatusCode.OK, arizalar.StatusCode);
        Assert.Equal(Xlsx.ContentType, arizalar.Content.Headers.ContentType?.MediaType);
        using (var kitap = new XLWorkbook(new MemoryStream(await arizalar.Content.ReadAsByteArrayAsync())))
        {
            var s = kitap.Worksheet(1);
            Assert.Equal("Başlık", s.Cell(1, 3).GetString());
            var hucre = s.Cell(2, 3);
            Assert.False(hucre.HasFormula);
            Assert.Equal(XLDataType.Text, hucre.DataType);
            Assert.Equal("=1+1", hucre.GetString());
            Assert.False(s.Cell(2, 2).HasFormula); // istasyon adı
        }

        var denetim = await yonetici.GetAsync("/denetim/disa-aktar.xlsx");
        Assert.Equal(HttpStatusCode.OK, denetim.StatusCode);
        using var kitap2 = new XLWorkbook(new MemoryStream(await denetim.Content.ReadAsByteArrayAsync()));
        Assert.Equal("Kullanıcı", kitap2.Worksheet(1).Cell(1, 3).GetString());
        Assert.False(kitap2.Worksheet(1).Cell(2, 1).IsEmpty());
        Assert.All(kitap2.Worksheet(1).CellsUsed(), c => Assert.False(c.HasFormula));
    }
}
