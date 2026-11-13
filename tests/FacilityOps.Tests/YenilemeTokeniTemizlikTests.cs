using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using FacilityOps.Api.Data;
using FacilityOps.Api.Domain;
using FacilityOps.Api.Services;

namespace FacilityOps.Tests;

public class YenilemeTokeniTemizlikTests : IDisposable
{
    private static readonly DateTime Simdi = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Saklama = TimeSpan.FromHours(24);

    private readonly SqliteConnection _conn = new("Data Source=:memory:");
    private readonly AppDbContext _db;

    public YenilemeTokeniTemizlikTests()
    {
        _conn.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
        _db.Kullanicilar.Add(new Kullanici { KullaniciAdi = "ayse", Rol = Roller.Personel });
        _db.SaveChanges();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    private void Ekle(string ozet, DateTime sonKullanma, DateTime? iptal = null)
    {
        _db.YenilemeTokenleri.Add(new YenilemeTokeni { KullaniciId = 1, TokenOzeti = ozet, SonKullanma = sonKullanma, IptalZamani = iptal });
        _db.SaveChanges();
    }

    private List<string> Kalanlar() => _db.YenilemeTokenleri.AsNoTracking().OrderBy(t => t.TokenOzeti).Select(t => t.TokenOzeti).ToList();

    [Fact]
    public async Task SaklamaSuresindenOnceDolanKayitlariSiler()
    {
        Ekle("a-eski", Simdi.AddDays(-3));          // 3 gün önce doldu → silinir
        Ekle("b-yeni-dolmus", Simdi.AddHours(-2));  // 2 saat önce doldu, saklama süresi içinde → kalır

        var silinen = await YenilemeTokeniTemizleyici.TemizleAsync(_db, Simdi, Saklama);

        Assert.Equal(1, silinen);
        Assert.Equal(["b-yeni-dolmus"], Kalanlar());
    }

    [Fact]
    public async Task GecerliKayitlaraDokunmaz()
    {
        Ekle("gecerli", Simdi.AddDays(5));

        Assert.Equal(0, await YenilemeTokeniTemizleyici.TemizleAsync(_db, Simdi, Saklama));
        Assert.Equal(["gecerli"], Kalanlar());
    }

    [Fact]
    public async Task IptalEdilmisAmaSuresiDolmamisKayit_TekrarKullanimTespitiIcinKalir()
    {
        // İptal edilmiş token 10 gün önce iptal edilmiş olsa da süresi dolmadığı sürece silinmemelidir.
        Ekle("iptal-ama-gecerli", Simdi.AddDays(2), iptal: Simdi.AddDays(-10));

        Assert.Equal(0, await YenilemeTokeniTemizleyici.TemizleAsync(_db, Simdi, Saklama));
        Assert.Equal(["iptal-ama-gecerli"], Kalanlar());
    }

    [Fact]
    public async Task BosTabloda_SifirDoner()
    {
        Assert.Equal(0, await YenilemeTokeniTemizleyici.TemizleAsync(_db, Simdi, Saklama));
    }
}
