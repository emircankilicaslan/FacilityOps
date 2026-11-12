using KamuOps.Api.Data;
using KamuOps.Api.Domain;
using KamuOps.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KamuOps.Tests;

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

    [Fact]
    public async Task Olustur_YeniKayit_AcikDurumdaBaslar()
    {
        var a = await _svc.OlusturAsync(new ArizaOlustur(1, "Sinyalizasyon arızası", null, Oncelik.Kritik));
        Assert.Equal(ArizaDurumu.Acik, a.Durum);
    }

    [Fact]
    public async Task Olustur_OlmayanIstasyon_HataFirlatir() =>
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _svc.OlusturAsync(new ArizaOlustur(99, "X", null, Oncelik.Normal)));

    [Fact]
    public async Task Olustur_BosBaslik_HataFirlatir() =>
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _svc.OlusturAsync(new ArizaOlustur(1, "  ", null, Oncelik.Normal)));

    [Fact]
    public async Task TamAkis_AcikTanKapandiyaGider()
    {
        var a = await _svc.OlusturAsync(new ArizaOlustur(1, "Trafo ısınması", null, Oncelik.Yuksek));
        await _svc.AtaAsync(a.Id, "Ayşe");
        await _svc.CozAsync(a.Id);
        var son = await _svc.KapatAsync(a.Id);

        Assert.Equal(ArizaDurumu.Kapandi, son.Durum);
        Assert.Equal("Ayşe", son.AtananKisi);
        Assert.NotNull(son.KapanmaZamani);
    }

    [Fact]
    public async Task Kapat_CozulmedenOnce_HataFirlatir()
    {
        var a = await _svc.OlusturAsync(new ArizaOlustur(1, "Radar", null, Oncelik.Normal));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.KapatAsync(a.Id));
    }

    [Fact]
    public async Task Listele_OnceyeGoreSiralar_VeFiltreler()
    {
        await _svc.OlusturAsync(new ArizaOlustur(1, "Düşük", null, Oncelik.Dusuk));
        await _svc.OlusturAsync(new ArizaOlustur(1, "Kritik", null, Oncelik.Kritik));

        var hepsi = await _svc.ListeleAsync(null, null);
        Assert.Equal("Kritik", hepsi[0].Baslik);

        var sadeceDusuk = await _svc.ListeleAsync(null, Oncelik.Dusuk);
        Assert.Single(sadeceDusuk);
    }
}
