using FacilityOps.Api.Data;
using FacilityOps.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FacilityOps.Api.Services;

/// <summary>Denetim kaydı yazımı. <see cref="Ekle"/> kaydı yalnızca bağlama ekler; çağıran, asıl değişiklikle aynı SaveChanges'te yazar (atomik).</summary>
public static class Denetim
{
    public static void Ekle(AppDbContext db, string? kullanici, string? rol, string islem,
        string? varlikTuru = null, object? varlikId = null, string? ayrinti = null)
    {
        db.DenetimKayitlari.Add(new DenetimKaydi
        {
            Zaman = DateTime.UtcNow,
            Kullanici = Kes(string.IsNullOrWhiteSpace(kullanici) ? "sistem" : kullanici, 50)!,
            Rol = Kes(rol, 20),
            Islem = Kes(islem, 40)!,
            VarlikTuru = Kes(varlikTuru, 20),
            VarlikId = Kes(varlikId?.ToString(), 40),
            Ayrinti = Kes(ayrinti, 500),
        });
    }

    private static string? Kes(string? deger, int uzunluk) =>
        deger is null ? null : deger.Length <= uzunluk ? deger : deger[..uzunluk];
}

/// <summary>Denetim kaydını sorgular (yalnızca Yönetici uçlarından çağrılır).</summary>
public class DenetimService(AppDbContext db)
{
    private IQueryable<DenetimKaydi> Filtrele(string? kullanici, string? islem, DateTime? baslangic, DateTime? bitis)
    {
        var sorgu = db.DenetimKayitlari.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(kullanici))
        {
            var ad = kullanici.Trim().ToLowerInvariant();
            sorgu = sorgu.Where(d => d.Kullanici.ToLower() == ad);
        }
        if (!string.IsNullOrWhiteSpace(islem)) { var i = islem.Trim(); sorgu = sorgu.Where(d => d.Islem == i); }
        if (baslangic is { } b) sorgu = sorgu.Where(d => d.Zaman >= b.ToUniversalTime());
        if (bitis is { } s) sorgu = sorgu.Where(d => d.Zaman < s.ToUniversalTime());
        return sorgu;
    }

    private static void Dogrula(DateTime? baslangic, DateTime? bitis)
    {
        if (baslangic is not null && bitis is not null && baslangic >= bitis)
            throw new ArgumentException("Başlangıç, bitişten önce olmalıdır.");
    }

    public async Task<Sayfali<DenetimKaydi>> ListeleAsync(string? kullanici, string? islem, DateTime? baslangic, DateTime? bitis, int sayfa, int boyut)
    {
        if (sayfa < 1) throw new ArgumentException("Sayfa 1 veya daha büyük olmalı.");
        if (boyut is < 1 or > 100) throw new ArgumentException("Sayfa boyutu 1-100 arasında olmalı.");
        Dogrula(baslangic, bitis);

        var sorgu = Filtrele(kullanici, islem, baslangic, bitis);
        var toplam = await sorgu.CountAsync();
        var ogeler = await sorgu.OrderByDescending(d => d.Zaman).ThenByDescending(d => d.Id)
            .Skip((sayfa - 1) * boyut).Take(boyut).ToListAsync();
        return new Sayfali<DenetimKaydi>(sayfa, boyut, toplam, ogeler);
    }

    private const int EnFazlaSatir = 10_000;

    public async Task<byte[]> XlsxAsync(string? kullanici, string? islem, DateTime? baslangic, DateTime? bitis)
    {
        Dogrula(baslangic, bitis);
        var liste = await Filtrele(kullanici, islem, baslangic, bitis)
            .OrderByDescending(d => d.Zaman).ThenByDescending(d => d.Id).Take(EnFazlaSatir).ToListAsync();
        return Xlsx.Olustur("Denetim kaydı",
            ["Id", "Zaman (UTC)", "Kullanıcı", "Rol", "İşlem", "Varlık türü", "Kayıt", "Ayrıntı"],
            liste.Select(d => (IReadOnlyList<object?>)[d.Id, d.Zaman, d.Kullanici, d.Rol, d.Islem, d.VarlikTuru, d.VarlikId, d.Ayrinti]));
    }
}
