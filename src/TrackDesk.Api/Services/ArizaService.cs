using System.Text;
using Microsoft.EntityFrameworkCore;
using TrackDesk.Api.Data;
using TrackDesk.Api.Domain;

namespace TrackDesk.Api.Services;

/// <summary>İş kurallarını (durum geçişleri, doğrulama, denetim kaydı) endpoint'lerden ayrı tutar; test edilebilir.</summary>
public class ArizaService(AppDbContext db)
{
    public const int MaksSayfaBoyutu = 100;

    public static TimeSpan SlaSuresi(Oncelik o) => o switch
    {
        Oncelik.Kritik => TimeSpan.FromHours(4),
        Oncelik.Yuksek => TimeSpan.FromHours(24),
        Oncelik.Normal => TimeSpan.FromHours(72),
        _ => TimeSpan.FromDays(7)
    };

    public async Task<ArizaKaydi> OlusturAsync(ArizaOlustur istek, string islemYapan)
    {
        if (string.IsNullOrWhiteSpace(istek.Baslik))
            throw new ArgumentException("Başlık boş olamaz.");
        if (istek.Baslik.Trim().Length > 200)
            throw new ArgumentException("Başlık en fazla 200 karakter olabilir.");
        if (istek.Aciklama?.Length > 2000)
            throw new ArgumentException("Açıklama en fazla 2000 karakter olabilir.");
        if (!Enum.IsDefined(istek.Oncelik))
            throw new ArgumentException("Geçersiz öncelik değeri.");

        if (!await db.Istasyonlar.AnyAsync(i => i.Id == istek.IstasyonId))
            throw new KeyNotFoundException("İstasyon bulunamadı.");

        var simdi = DateTime.UtcNow;
        var ariza = new ArizaKaydi
        {
            IstasyonId = istek.IstasyonId,
            Baslik = istek.Baslik.Trim(),
            Aciklama = istek.Aciklama,
            Oncelik = istek.Oncelik,
            OlusturmaZamani = simdi,
            SlaSonTarih = simdi + SlaSuresi(istek.Oncelik)
        };
        await using var tx = await db.Database.BeginTransactionAsync();
        db.Arizalar.Add(ariza);
        await db.SaveChangesAsync(); // Id oluşsun
        await GecmiseYazAsync(ariza, "Olusturuldu", null, islemYapan);
        await tx.CommitAsync();
        return ariza;
    }

    public async Task<ArizaKaydi> AtaAsync(int id, string kisi, string islemYapan)
    {
        if (string.IsNullOrWhiteSpace(kisi))
            throw new ArgumentException("Atanacak kişi boş olamaz.");
        if (kisi.Trim().Length > 100)
            throw new ArgumentException("Atanacak kişi adı en fazla 100 karakter olabilir.");

        var ariza = await GetirAsync(id);
        var eski = ariza.Durum;
        GecisKontrol(ariza, ArizaDurumu.Atandi);
        ariza.AtananKisi = kisi.Trim();
        ariza.Durum = ArizaDurumu.Atandi;
        await GecmiseYazAsync(ariza, "Atandi", eski, islemYapan);
        return ariza;
    }

    public async Task<ArizaKaydi> CozAsync(int id, string islemYapan)
    {
        var ariza = await GetirAsync(id);
        var eski = ariza.Durum;
        GecisKontrol(ariza, ArizaDurumu.Cozuldu);
        ariza.Durum = ArizaDurumu.Cozuldu;
        await GecmiseYazAsync(ariza, "Cozuldu", eski, islemYapan);
        return ariza;
    }

    public async Task<ArizaKaydi> KapatAsync(int id, string islemYapan)
    {
        var ariza = await GetirAsync(id);
        var eski = ariza.Durum;
        GecisKontrol(ariza, ArizaDurumu.Kapandi);
        ariza.Durum = ArizaDurumu.Kapandi;
        ariza.KapanmaZamani = DateTime.UtcNow;
        await GecmiseYazAsync(ariza, "Kapandi", eski, islemYapan);
        return ariza;
    }

    public async Task<ArizaKaydi> DetayAsync(int id) => await db.Arizalar.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id) ?? throw new KeyNotFoundException("Arıza kaydı bulunamadı.");
    
    public async Task<Sayfali<ArizaKaydi>> ListeleAsync(
        ArizaDurumu? durum, Oncelik? oncelik, bool? gecikmis, int sayfa = 1, int boyut = 20, string? atanan = null, string? ara = null)
    {
        if (sayfa < 1) throw new ArgumentException("Sayfa 1 veya daha büyük olmalı.");
        if (boyut < 1 || boyut > MaksSayfaBoyutu)
            throw new ArgumentException($"Sayfa boyutu 1 ile {MaksSayfaBoyutu} arasında olmalı.");

        var sorgu = Filtrele(durum, oncelik, gecikmis, atanan, ara);
        var toplam = await sorgu.CountAsync();
        var ogeler = await sorgu
            .OrderByDescending(a => a.Oncelik).ThenBy(a => a.OlusturmaZamani).ThenBy(a => a.Id)
            .Skip((sayfa - 1) * boyut).Take(boyut)
            .ToListAsync();
        return new Sayfali<ArizaKaydi>(sayfa, boyut, toplam, ogeler);
    }

    public async Task<ArizaOzeti> OzetAsync()
    {
        var durumlar = (await db.Arizalar.GroupBy(a => a.Durum)
                .Select(g => new { g.Key, Say = g.Count() }).ToListAsync())
            .ToDictionary(x => x.Key.ToString(), x => x.Say);
        var simdi = DateTime.UtcNow;
        var gecikmis = await db.Arizalar.CountAsync(a => a.Durum != ArizaDurumu.Kapandi && a.SlaSonTarih < simdi);
        return new ArizaOzeti(durumlar, gecikmis);
    }

    public async Task<List<ArizaGecmisi>> GecmisAsync(int id)
    {
        if (!await db.Arizalar.AnyAsync(a => a.Id == id))
            throw new KeyNotFoundException("Arıza kaydı bulunamadı.");
        return await db.ArizaGecmisleri.AsNoTracking()
            .Where(g => g.ArizaId == id).OrderBy(g => g.Zaman).ThenBy(g => g.Id).ToListAsync();
    }

    /// <summary>Tüm filtreye uyan kayıtları Excel'de açılabilir CSV (UTF-8 BOM) olarak üretir.</summary>
    public async Task<byte[]> CsvAsync(ArizaDurumu? durum, Oncelik? oncelik, bool? gecikmis)
    {
        var liste = await Filtrele(durum, oncelik, gecikmis).Include(a => a.Istasyon)
            .OrderBy(a => a.Id).ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("Id;Istasyon;Baslik;Oncelik;Durum;AtananKisi;Olusturma;SlaSonTarih;Gecikmis");
        foreach (var a in liste)
            sb.AppendLine(string.Join(';',
                a.Id, Csv(a.Istasyon?.Ad), Csv(a.Baslik), a.Oncelik, a.Durum, Csv(a.AtananKisi),
                a.OlusturmaZamani.ToString("yyyy-MM-dd HH:mm"), a.SlaSonTarih.ToString("yyyy-MM-dd HH:mm"),
                a.Gecikmis ? "Evet" : "Hayir"));

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    /// <summary>CSV alanını kaçışlar; =, +, -, @ ile başlayan değerlere ' ekleyerek formül enjeksiyonunu önler.</summary>
    public static string Csv(string? deger)
    {
        if (string.IsNullOrEmpty(deger)) return "";
        if ("=+-@\t\r".Contains(deger[0])) deger = "'" + deger;
        return deger.Contains(';') || deger.Contains('"') || deger.Contains('\n')
            ? "\"" + deger.Replace("\"", "\"\"") + "\""
            : deger;
    }

    private IQueryable<ArizaKaydi> Filtrele(ArizaDurumu? durum, Oncelik? oncelik, bool? gecikmis, string? atanan = null, string? ara = null)
    {
        var sorgu = db.Arizalar.AsNoTracking().AsQueryable();
        if (durum is not null) sorgu = sorgu.Where(a => a.Durum == durum);
        if (oncelik is not null) sorgu = sorgu.Where(a => a.Oncelik == oncelik);
        if (gecikmis is not null)
        {
            var simdi = DateTime.UtcNow;
            sorgu = gecikmis.Value
                ? sorgu.Where(a => a.Durum != ArizaDurumu.Kapandi && a.SlaSonTarih < simdi)
                : sorgu.Where(a => a.Durum == ArizaDurumu.Kapandi || a.SlaSonTarih >= simdi);
        }
        if (!string.IsNullOrWhiteSpace(atanan)) sorgu = sorgu.Where(a => a.AtananKisi != null && a.AtananKisi.ToLower() == atanan.Trim().ToLower());
        if (!string.IsNullOrWhiteSpace(ara)) sorgu = sorgu.Where(a => a.Baslik.ToLower().Contains(ara.Trim().ToLower()));
        return sorgu;
    }

    /// <summary>Bekleyen durum değişikliği ile denetim kaydını aynı SaveChanges'te (atomik) yazar.</summary>
    private async Task GecmiseYazAsync(ArizaKaydi ariza, string islem, ArizaDurumu? eski, string islemYapan)
    {
        db.ArizaGecmisleri.Add(new ArizaGecmisi
        {
            ArizaId = ariza.Id,
            Islem = islem,
            EskiDurum = eski,
            YeniDurum = ariza.Durum,
            Kullanici = string.IsNullOrWhiteSpace(islemYapan) ? "sistem" : islemYapan,
            Zaman = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private async Task<ArizaKaydi> GetirAsync(int id) =>
        await db.Arizalar.FindAsync(id) ?? throw new KeyNotFoundException("Arıza kaydı bulunamadı.");

    // Açık -> Atandı -> Çözüldü -> Kapandı ; atlama veya geri dönüş yok (yeniden atama hariç).
    private static void GecisKontrol(ArizaKaydi a, ArizaDurumu hedef)
    {
        var gecerli = (a.Durum, hedef) switch
        {
            (ArizaDurumu.Acik, ArizaDurumu.Atandi) => true,
            (ArizaDurumu.Atandi, ArizaDurumu.Atandi) => true,
            (ArizaDurumu.Atandi, ArizaDurumu.Cozuldu) => true,
            (ArizaDurumu.Cozuldu, ArizaDurumu.Kapandi) => true,
            _ => false
        };
        if (!gecerli)
            throw new InvalidOperationException($"{a.Durum} durumundan {hedef} durumuna geçilemez.");
    }
}
