using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using FacilityOps.Api.Data;
using FacilityOps.Api.Domain;
using FacilityOps.Api.Services;

namespace FacilityOps.Tests;

public class ArizaFiltreTests : IDisposable
{
    private readonly SqliteConnection _conn = new("Data Source=:memory:");
    private readonly AppDbContext _db;
    private readonly ArizaService _svc;

    public ArizaFiltreTests()
    {
        _conn.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
        _db.Istasyonlar.Add(new Istasyon { Ad = "Ankara Garı", Sehir = "Ankara" });
        _db.SaveChanges();
        _svc = new ArizaService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    private Task<ArizaKaydi> Yeni(string baslik) =>
        _svc.OlusturAsync(new ArizaOlustur(1, baslik, null, Oncelik.Normal), "test");

    [Fact]
    public async Task Listele_AtananFiltresi_SadeceAtananKisininKayitlariniDoner()
    {
        var a = await Yeni("Sinyal");
        var b = await Yeni("Radar");
        await Yeni("Atanmamış");
        await _svc.AtaAsync(a.Id, "Ayse", "mudur");
        await _svc.AtaAsync(b.Id, "Veli", "mudur");

        var sonuc = await _svc.ListeleAsync(null, null, null, atanan: "AYSE");

        Assert.Equal(["Sinyal"], sonuc.Ogeler.Select(x => x.Baslik));
        Assert.Equal(1, sonuc.Toplam);
    }

    [Fact]
    public async Task Listele_AraFiltresi_BasliktaGecenMetniBulur_BuyukKucukHarfDuyarsizdir()
    {
        await Yeni("Sinyalizasyon arızası");
        await Yeni("Pist ışıkları");

        var sonuc = await _svc.ListeleAsync(null, null, null, ara: "SINYAL");

        Assert.Equal(["Sinyalizasyon arızası"], sonuc.Ogeler.Select(x => x.Baslik));
    }

    [Fact]
    public async Task Listele_FiltreleriBirlestirir()
    {
        var a = await Yeni("Sinyal 1");
        var b = await Yeni("Sinyal 2");
        await Yeni("Radar");
        await _svc.AtaAsync(a.Id, "Ayse", "mudur");
        await _svc.AtaAsync(b.Id, "Veli", "mudur");

        var sonuc = await _svc.ListeleAsync(null, null, null, atanan: "veli", ara: "sinyal");

        Assert.Equal(["Sinyal 2"], sonuc.Ogeler.Select(x => x.Baslik));
    }

    [Fact]
    public async Task Listele_BosVeyaBosluktanOlusanFiltre_FiltrelemezVeEskiCagrilarCalisir()
    {
        await Yeni("A");
        await Yeni("B");

        var bos = await _svc.ListeleAsync(null, null, null, atanan: "  ", ara: "");
        var eskiCagri = await _svc.ListeleAsync(null, null, null, 1, 20);

        Assert.Equal(2, bos.Toplam);
        Assert.Equal(2, eskiCagri.Toplam);
    }

    [Fact]
    public async Task Detay_VarOlanKayit_Doner()
    {
        var a = await Yeni("Detay");

        var sonuc = await _svc.DetayAsync(a.Id);

        Assert.Equal("Detay", sonuc.Baslik);
        Assert.False(sonuc.Gecikmis);
    }

    [Fact]
    public async Task Detay_OlmayanKayit_HataFirlatir() =>
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _svc.DetayAsync(999));
}
