using Microsoft.EntityFrameworkCore;
using FacilityOps.Api.Data;
using FacilityOps.Api.Domain;

namespace FacilityOps.Api.Services;

/// <summary>Yönetici işlemleri: kullanıcı listesi, rol ve durum değişikliği.</summary>
public class KullaniciService(AppDbContext db, AuthService auth)
{
    public Task<List<KullaniciOzeti>> ListeleAsync() =>
        db.Kullanicilar.AsNoTracking()
            .OrderBy(k => k.KullaniciAdi)
            .Select(k => new KullaniciOzeti(k.Id, k.KullaniciAdi, k.Rol, k.Aktif))
            .ToListAsync();

    public async Task<KullaniciOzeti> RolDegistirAsync(int id, string? rol)
    {
        if (rol is not (Roller.Personel or Roller.Yonetici))
            throw new ArgumentException($"Rol '{Roller.Personel}' veya '{Roller.Yonetici}' olmalı.");

        var k = await Bul(id);
        if (k.Rol == Roller.Yonetici && rol != Roller.Yonetici)
            await SonYoneticiDegilseAsync(k);

        k.Rol = rol;
        await db.SaveChangesAsync();
        // Eski rolle verilmiş token en fazla erişim süresi kadar geçerli kalır; yenileme tokenları kapatılır.
        await auth.TumOturumlariKapatAsync(k.Id);
        return Ozet(k);
    }

    public async Task<KullaniciOzeti> DurumDegistirAsync(int id, bool aktif)
    {
        var k = await Bul(id);
        if (!aktif && k.Aktif && k.Rol == Roller.Yonetici)
            await SonYoneticiDegilseAsync(k);

        k.Aktif = aktif;
        await db.SaveChangesAsync();
        if (!aktif) await auth.TumOturumlariKapatAsync(k.Id);
        return Ozet(k);
    }

    private async Task<Kullanici> Bul(int id) =>
        await db.Kullanicilar.FindAsync(id) ?? throw new KeyNotFoundException("Kullanıcı bulunamadı.");

    /// <summary>Sistemde en az bir aktif yönetici kalmalı; aksi halde yönetici işlemleri kilitlenir.</summary>
    private async Task SonYoneticiDegilseAsync(Kullanici k)
    {
        var digerAktifYonetici = await db.Kullanicilar.AnyAsync(x => x.Id != k.Id && x.Rol == Roller.Yonetici && x.Aktif);
        if (!digerAktifYonetici)
            throw new InvalidOperationException("Son aktif yönetici pasife alınamaz veya rolü düşürülemez.");
    }

    private static KullaniciOzeti Ozet(Kullanici k) => new(k.Id, k.KullaniciAdi, k.Rol, k.Aktif);
}
