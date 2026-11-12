using TrackDesk.Api.Data;
using TrackDesk.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace TrackDesk.Api.Services;

/// <summary>İş kurallarını (durum geçişleri, doğrulama) endpoint'lerden ayrı tutar; test edilebilir.</summary>
public class ArizaService(AppDbContext db)
{
    public async Task<ArizaKaydi> OlusturAsync(ArizaOlustur istek)
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

        var ariza = new ArizaKaydi
        {
            IstasyonId = istek.IstasyonId,
            Baslik = istek.Baslik.Trim(),
            Aciklama = istek.Aciklama,
            Oncelik = istek.Oncelik
        };
        db.Arizalar.Add(ariza);
        await db.SaveChangesAsync();
        return ariza;
    }

    public async Task<ArizaKaydi> AtaAsync(int id, string kisi)
    {
        if (string.IsNullOrWhiteSpace(kisi))
            throw new ArgumentException("Atanacak kişi boş olamaz.");

        var ariza = await GetirAsync(id);
        GecisKontrol(ariza, ArizaDurumu.Atandi);
        ariza.AtananKisi = kisi.Trim();
        ariza.Durum = ArizaDurumu.Atandi;
        await db.SaveChangesAsync();
        return ariza;
    }

    public async Task<ArizaKaydi> CozAsync(int id)
    {
        var ariza = await GetirAsync(id);
        GecisKontrol(ariza, ArizaDurumu.Cozuldu);
        ariza.Durum = ArizaDurumu.Cozuldu;
        await db.SaveChangesAsync();
        return ariza;
    }

    public async Task<ArizaKaydi> KapatAsync(int id)
    {
        var ariza = await GetirAsync(id);
        GecisKontrol(ariza, ArizaDurumu.Kapandi);
        ariza.Durum = ArizaDurumu.Kapandi;
        ariza.KapanmaZamani = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return ariza;
    }

    public async Task<List<ArizaKaydi>> ListeleAsync(ArizaDurumu? durum, Oncelik? oncelik)
    {
        var sorgu = db.Arizalar.AsNoTracking().AsQueryable();
        if (durum is not null) sorgu = sorgu.Where(a => a.Durum == durum);
        if (oncelik is not null) sorgu = sorgu.Where(a => a.Oncelik == oncelik);
        return await sorgu.OrderByDescending(a => a.Oncelik).ThenBy(a => a.OlusturmaZamani).ToListAsync();
    }

    public async Task<Dictionary<string, int>> OzetAsync() =>
        (await db.Arizalar.GroupBy(a => a.Durum).Select(g => new { g.Key, Say = g.Count() }).ToListAsync())
        .ToDictionary(x => x.Key.ToString(), x => x.Say);

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
