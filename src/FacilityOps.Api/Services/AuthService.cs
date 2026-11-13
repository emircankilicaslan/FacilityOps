using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using FacilityOps.Api.Data;
using FacilityOps.Api.Domain;

namespace FacilityOps.Api.Services;

public class AuthService(AppDbContext db, IConfiguration config)
{
    private const int Iterasyon = 100_000;

    private int ErisimDakika => config.GetValue("Jwt:ErisimDakika", 15);
    private int YenilemeGun => config.GetValue("Jwt:YenilemeGun", 7);

    /// <summary>İlk kayıtlı kullanıcı Yonetici olur, sonrakiler Personel.</summary>
    public async Task<Kullanici> KayitAsync(KayitIstegi istek)
    {
        if (string.IsNullOrWhiteSpace(istek.KullaniciAdi) || istek.KullaniciAdi.Length < 3)
            throw new ArgumentException("Kullanıcı adı en az 3 karakter olmalı.");
        SifreDogrula(istek.Sifre);

        var ad = istek.KullaniciAdi.Trim().ToLowerInvariant();
        if (await db.Kullanicilar.AnyAsync(k => k.KullaniciAdi == ad))
            throw new InvalidOperationException("Bu kullanıcı adı alınmış.");

        var kullanici = new Kullanici
        {
            KullaniciAdi = ad,
            Rol = await db.Kullanicilar.AnyAsync() ? Roller.Personel : Roller.Yonetici
        };
        kullanici.SifreHash = Hashle(istek.Sifre, out var tuz);
        kullanici.SifreTuz = tuz;
        db.Kullanicilar.Add(kullanici);
        await db.SaveChangesAsync();
        return kullanici;
    }

    public async Task<TokenCifti> GirisAsync(GirisIstegi istek)
    {
        var ad = (istek.KullaniciAdi ?? "").Trim().ToLowerInvariant();
        var k = await db.Kullanicilar.FirstOrDefaultAsync(x => x.KullaniciAdi == ad);
        if (k is null || !k.Aktif || !SifreDogru(istek.Sifre ?? "", k))
            throw new UnauthorizedAccessException("Kullanıcı adı veya şifre hatalı.");
        return await CiftUretAsync(k);
    }

    /// <summary>
    /// Yenileme tokenını döndürür: eski token iptal edilir, yeni bir erişim + yenileme çifti verilir.
    /// İptal edilmiş bir token tekrar sunulursa çalınmış olabileceği varsayılır ve kullanıcının tüm oturumları kapatılır.
    /// </summary>
    public async Task<TokenCifti> YenileAsync(string? yenilemeTokeni)
    {
        if (string.IsNullOrWhiteSpace(yenilemeTokeni))
            throw new UnauthorizedAccessException("Yenileme tokenı gerekli.");

        var ozet = Ozet(yenilemeTokeni);
        var t = await db.YenilemeTokenleri.Include(x => x.Kullanici).FirstOrDefaultAsync(x => x.TokenOzeti == ozet)
            ?? throw new UnauthorizedAccessException("Yenileme tokenı geçersiz.");

        if (t.IptalZamani is not null)
        {
            await TumOturumlariKapatAsync(t.KullaniciId);
            throw new UnauthorizedAccessException("Yenileme tokenı daha önce kullanılmış; tüm oturumlar kapatıldı.");
        }
        if (t.SonKullanma <= DateTime.UtcNow || !t.Kullanici.Aktif)
            throw new UnauthorizedAccessException("Yenileme tokenı geçersiz.");

        t.IptalZamani = DateTime.UtcNow;
        return await CiftUretAsync(t.Kullanici); // iptal ile yeni token aynı SaveChanges'ta yazılır
    }

    /// <summary>Tek bir yenileme tokenını iptal eder. Bilinmeyen token sessizce yok sayılır.</summary>
    public async Task CikisAsync(string? yenilemeTokeni)
    {
        if (string.IsNullOrWhiteSpace(yenilemeTokeni)) return;
        var ozet = Ozet(yenilemeTokeni);
        var t = await db.YenilemeTokenleri.FirstOrDefaultAsync(x => x.TokenOzeti == ozet && x.IptalZamani == null);
        if (t is null) return;
        t.IptalZamani = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task SifreDegistirAsync(int kullaniciId, string? mevcutSifre, string? yeniSifre)
    {
        var k = await db.Kullanicilar.FindAsync(kullaniciId) ?? throw new KeyNotFoundException("Kullanıcı bulunamadı.");
        if (!SifreDogru(mevcutSifre ?? "", k))
            throw new ArgumentException("Mevcut şifre hatalı.");
        SifreDogrula(yeniSifre);
        if (mevcutSifre == yeniSifre)
            throw new ArgumentException("Yeni şifre mevcut şifreden farklı olmalı.");

        k.SifreHash = Hashle(yeniSifre!, out var tuz);
        k.SifreTuz = tuz;
        await db.SaveChangesAsync();
        await TumOturumlariKapatAsync(k.Id);
    }

    public async Task TumOturumlariKapatAsync(int kullaniciId)
    {
        var simdi = DateTime.UtcNow;
        var acik = await db.YenilemeTokenleri.Where(t => t.KullaniciId == kullaniciId && t.IptalZamani == null).ToListAsync();
        foreach (var t in acik) t.IptalZamani = simdi;
        await db.SaveChangesAsync();
    }

    private async Task<TokenCifti> CiftUretAsync(Kullanici k)
    {
        // Süresi dolmuş kayıtlar her yenilemede temizlenir; tablo sınırsız büyümez.
        var sinir = DateTime.UtcNow.AddDays(-1);
        db.YenilemeTokenleri.RemoveRange(db.YenilemeTokenleri.Where(t => t.KullaniciId == k.Id && t.SonKullanma < sinir));

        var ham = Base64Url(RandomNumberGenerator.GetBytes(32));
        db.YenilemeTokenleri.Add(new YenilemeTokeni
        {
            KullaniciId = k.Id,
            TokenOzeti = Ozet(ham),
            SonKullanma = DateTime.UtcNow.AddDays(YenilemeGun)
        });
        await db.SaveChangesAsync();

        var son = DateTime.UtcNow.AddMinutes(ErisimDakika);
        return new TokenCifti(ErisimTokeniUret(k, son), ham, son);
    }

    private string ErisimTokeniUret(Kullanici k, DateTime sonKullanma)
    {
        var anahtar = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, k.Id.ToString()),
                new Claim(ClaimTypes.Name, k.KullaniciAdi),
                new Claim(ClaimTypes.Role, k.Rol)
            ],
            expires: sonKullanma,
            signingCredentials: new SigningCredentials(anahtar, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static void SifreDogrula(string? sifre)
    {
        if (string.IsNullOrEmpty(sifre) || sifre.Length < 8)
            throw new ArgumentException("Şifre en az 8 karakter olmalı.");
    }

    private static string Ozet(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static string Base64Url(byte[] b) =>
        Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Hashle(string sifre, out byte[] tuz)
    {
        tuz = RandomNumberGenerator.GetBytes(16);
        return Rfc2898DeriveBytes.Pbkdf2(sifre, tuz, Iterasyon, HashAlgorithmName.SHA256, 32);
    }

    private static bool SifreDogru(string sifre, Kullanici k) =>
        CryptographicOperations.FixedTimeEquals(
            Rfc2898DeriveBytes.Pbkdf2(sifre, k.SifreTuz, Iterasyon, HashAlgorithmName.SHA256, 32),
            k.SifreHash);
}
