using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TrackDesk.Api.Data;
using TrackDesk.Api.Domain;
using TrackDesk.Api.Services;

namespace TrackDesk.Tests;

public class ArizaAtamaDogrulamaTests : IDisposable
{
    private readonly SqliteConnection _conn = new("Data Source=:memory:");
    private readonly AppDbContext _db;
    private readonly ArizaService _svc;

    public ArizaAtamaDogrulamaTests()
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

    private Task<ArizaKaydi> Yeni() =>
        _svc.OlusturAsync(new ArizaOlustur(1, "Arıza", null, Oncelik.Normal), "test");

    [Fact]
    public async Task Ata_YuzKarakterlikAd_KabulEdilir()
    {
        var a = await Yeni();

        var sonuc = await _svc.AtaAsync(a.Id, new string('a', 100), "mudur");

        Assert.Equal(100, sonuc.AtananKisi!.Length);
    }

    [Fact]
    public async Task Ata_YuzBirKarakterlikAd_HataFirlatir()
    {
        var a = await Yeni();

        await Assert.ThrowsAsync<ArgumentException>(() => _svc.AtaAsync(a.Id, new string('a', 101), "mudur"));
    }

    [Fact]
    public async Task Ata_ReddedilenAtama_DurumuVeGecmisiDegistirmez()
    {
        var a = await Yeni();

        await Assert.ThrowsAsync<ArgumentException>(() => _svc.AtaAsync(a.Id, new string('a', 101), "mudur"));

        var kayit = await _db.Arizalar.AsNoTracking().SingleAsync(x => x.Id == a.Id);
        Assert.Equal(ArizaDurumu.Acik, kayit.Durum);
        Assert.Null(kayit.AtananKisi);
        Assert.Single(await _svc.GecmisAsync(a.Id));
    }
}
