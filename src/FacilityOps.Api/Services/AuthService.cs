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

    /// <summary>İlk kayıtlı kullanıcı Yonetici olur, sonrakiler Personel.</summary>
    public async Task<Kullanici> KayitAsync(KayitIstegi istek)
    {
        if (string.IsNullOrWhiteSpace(istek.KullaniciAdi) || istek.KullaniciAdi.Length < 3)
            throw new ArgumentException("Kullanıcı adı en az 3 karakter olmalı.");
        if (string.IsNullOrEmpty(istek.Sifre) || istek.Sifre.Length < 8)
            throw new ArgumentException("Şifre en az 8 karakter olmalı.");

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

    public async Task<string> GirisAsync(GirisIstegi istek)
    {
        var ad = (istek.KullaniciAdi ?? "").Trim().ToLowerInvariant();
        var k = await db.Kullanicilar.FirstOrDefaultAsync(x => x.KullaniciAdi == ad);
        if (k is null || !SifreDogru(istek.Sifre ?? "", k))
            throw new UnauthorizedAccessException("Kullanıcı adı veya şifre hatalı.");
        return TokenUret(k);
    }

    private string TokenUret(Kullanici k)
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
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: new SigningCredentials(anahtar, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

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
