using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using FacilityOps.Api.Data;
using FacilityOps.Api.Domain;
using FacilityOps.Api.Services;

namespace FacilityOps.Tests;

public class ArizaServiceTests : IDisposable
{
    private readonly SqliteConnection _conn = new("Data Source=:memory:");
    private readonly AppDbContext _db;
    private readonly ArizaService _svc;

    public ArizaServiceTests()
    {
        _conn.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
        _db.Istasyonlar.Add(new Istasyon { Ad = "Ankara Garı", Sehir = "Ankara" });
        _db.SaveChanges();
        _svc = new ArizaService(_db);
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    private Task<ArizaKaydi> Yeni(string baslik = "Arıza", Oncelik o = Oncelik.Normal) =>
        _svc.OlusturAsync(new ArizaOlustur(1, baslik, null, o), "test");

    [Fact]
    public async Task Olustur_YeniKayit_AcikDurumdaBaslar()
    {
        var a = await Yeni("Sinyalizasyon arızası", Oncelik.Kritik);
        Assert.Equal(ArizaDurumu.Acik, a.Durum);
    }

    [Fact]
    public async Task Olustur_OlmayanIstasyon_HataFirlatir() =>
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _svc.OlusturAsync(new ArizaOlustur(99, "X", null, Oncelik.Normal), "test"));

    [Fact]
    public async Task Olustur_BosBaslik_HataFirlatir() =>
        await Assert.ThrowsAsync<ArgumentException>(() => Yeni("  "));

    [Fact]
    public async Task Olustur_GecersizOncelik_HataFirlatir() =>
        await Assert.ThrowsAsync<ArgumentException>(() => Yeni("X", (Oncelik)99));

    [Fact]
    public async Task Olustur_CokUzunBaslik_HataFirlatir() =>
        await Assert.ThrowsAsync<ArgumentException>(() => Yeni(new string('a', 201)));

    [Theory]
    [InlineData(Oncelik.Kritik, 4)]
    [InlineData(Oncelik.Yuksek, 24)]
    [InlineData(Oncelik.Normal, 72)]
    [InlineData(Oncelik.Dusuk, 168)]
    public async Task Olustur_SlaSonTarihi_OnceligeGoreHesaplanir(Oncelik o, int saat)
    {
        var a = await Yeni("SLA", o);
        Assert.Equal(TimeSpan.FromHours(saat), a.SlaSonTarih - a.OlusturmaZamani);
    }

    [Fact]
    public async Task TamAkis_AcikTanKapandiyaGider()
    {
        var a = await Yeni("Trafo ısınması", Oncelik.Yuksek);
        await _svc.AtaAsync(a.Id, "Ayşe", "mudur");
        await _svc.CozAsync(a.Id, "ayse");
        var son = await _svc.KapatAsync(a.Id, "mudur");

        Assert.Equal(ArizaDurumu.Kapandi, son.Durum);
        Assert.Equal("Ayşe", son.AtananKisi);
        Assert.NotNull(son.KapanmaZamani);
    }

    [Fact]
    public async Task Kapat_CozulmedenOnce_HataFirlatir()
    {
        var a = await Yeni("Radar");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.KapatAsync(a.Id, "mudur"));
    }

    [Fact]
    public async Task Gecmis_HerIslemiKullaniciylaKaydeder()
    {
        var a = await Yeni("Denetim");
        await _svc.AtaAsync(a.Id, "Ayşe", "mudur");
        await _svc.CozAsync(a.Id, "ayse");

        var gecmis = await _svc.GecmisAsync(a.Id);

        Assert.Equal(["Olusturuldu", "Atandi", "Cozuldu"], gecmis.Select(g => g.Islem));
        Assert.Equal(["test", "mudur", "ayse"], gecmis.Select(g => g.Kullanici));
        Assert.Equal(ArizaDurumu.Atandi, gecmis[2].EskiDurum);
        Assert.Equal(ArizaDurumu.Cozuldu, gecmis[2].YeniDurum);
    }

    [Fact]
    public async Task Gecmis_BasarisizGecis_KayitEklemez()
    {
        var a = await Yeni("Geçersiz geçiş");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.CozAsync(a.Id, "x"));
        Assert.Single(await _svc.GecmisAsync(a.Id));
    }

    [Fact]
    public async Task Gecmis_OlmayanAriza_HataFirlatir() =>
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _svc.GecmisAsync(999));

    [Fact]
    public async Task Listele_OnceyeGoreSiralar_VeFiltreler()
    {
        await Yeni("Düşük", Oncelik.Dusuk);
        await Yeni("Kritik", Oncelik.Kritik);

        var hepsi = await _svc.ListeleAsync(null, null, null);
        Assert.Equal("Kritik", hepsi.Ogeler[0].Baslik);

        var sadeceDusuk = await _svc.ListeleAsync(null, Oncelik.Dusuk, null);
        Assert.Single(sadeceDusuk.Ogeler);
    }

    [Fact]
    public async Task Listele_Sayfalama_DogruSayfayiVeToplamiDoner()
    {
        for (var i = 0; i < 25; i++) await Yeni($"Arıza {i}");

        var s3 = await _svc.ListeleAsync(null, null, null, sayfa: 3, boyut: 10);

        Assert.Equal(25, s3.Toplam);
        Assert.Equal(3, s3.ToplamSayfa);
        Assert.Equal(5, s3.Ogeler.Count);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task Listele_GecersizSayfaParametresi_HataFirlatir(int sayfa, int boyut) =>
        await Assert.ThrowsAsync<ArgumentException>(() => _svc.ListeleAsync(null, null, null, sayfa, boyut));

    [Fact]
    public async Task Gecikmis_SlaGecenAcikKayit_ListelenirKapaliOlanListelenmez()
    {
        var gec = await Yeni("Geciken", Oncelik.Kritik);
        await Yeni("Zamanında");
        var kapali = await Yeni("Kapalı");
        await _svc.AtaAsync(kapali.Id, "x", "u");
        await _svc.CozAsync(kapali.Id, "u");
        await _svc.KapatAsync(kapali.Id, "u");

        foreach (var id in new[] { gec.Id, kapali.Id })
            (await _db.Arizalar.FindAsync(id))!.SlaSonTarih = DateTime.UtcNow.AddHours(-1);
        await _db.SaveChangesAsync();

        var liste = await _svc.ListeleAsync(null, null, gecikmis: true);
        Assert.Equal(["Geciken"], liste.Ogeler.Select(a => a.Baslik));
        Assert.True(liste.Ogeler[0].Gecikmis);
        Assert.Equal(1, (await _svc.OzetAsync()).Gecikmis);
    }

    [Fact]
    public async Task Ozet_DurumSayilariniVerir()
    {
        var a = await Yeni("A");
        await Yeni("B");
        await _svc.AtaAsync(a.Id, "x", "u");

        var ozet = await _svc.OzetAsync();

        Assert.Equal(1, ozet.Durumlar["Atandi"]);
        Assert.Equal(1, ozet.Durumlar["Acik"]);
    }

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("a;b", "\"a;b\"")]
    [InlineData("o \"x\"", "\"o \"\"x\"\"\"")]
    [InlineData(null, "")]
    [InlineData("normal", "normal")]
    public void Csv_AlanlariKacisler_FormulEnjeksiyonunuEngeller(string? girdi, string beklenen) =>
        Assert.Equal(beklenen, ArizaService.Csv(girdi));

    [Fact]
    public async Task Csv_BasliklariVeSatirlariIcerir_BomIleBaslar()
    {
        await Yeni("=cmd|' /C calc'!A0");
        var bytes = await _svc.CsvAsync(null, null, null);

        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        var metin = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.StartsWith("Id;Istasyon;Baslik", metin);
        Assert.Contains("'=cmd", metin);
    }
}
