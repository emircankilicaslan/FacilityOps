using System.ComponentModel.DataAnnotations;

namespace TrackDesk.Api.Domain;

public enum ArizaDurumu { Acik = 0, Atandi = 1, Cozuldu = 2, Kapandi = 3 }

public enum Oncelik { Dusuk = 0, Normal = 1, Yuksek = 2, Kritik = 3 }

public class Istasyon
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Ad { get; set; } = "";

    [Required, MaxLength(50)]
    public string Sehir { get; set; } = "";

    public List<ArizaKaydi> Arizalar { get; set; } = new();
}

public class ArizaKaydi
{
    public int Id { get; set; }
    public int IstasyonId { get; set; }
    public Istasyon? Istasyon { get; set; }

    [Required, MaxLength(200)]
    public string Baslik { get; set; } = "";

    [MaxLength(2000)]
    public string? Aciklama { get; set; }

    public Oncelik Oncelik { get; set; } = Oncelik.Normal;
    public ArizaDurumu Durum { get; set; } = ArizaDurumu.Acik;

    [MaxLength(100)]
    public string? AtananKisi { get; set; }

    public DateTime OlusturmaZamani { get; set; } = DateTime.UtcNow;
    public DateTime? KapanmaZamani { get; set; }
}

public static class Roller
{
    public const string Personel = "Personel";
    public const string Yonetici = "Yonetici";
}

public class Kullanici
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string KullaniciAdi { get; set; } = "";

    public byte[] SifreHash { get; set; } = [];
    public byte[] SifreTuz { get; set; } = [];

    [MaxLength(20)]
    public string Rol { get; set; } = Roller.Personel;
}

public record KayitIstegi(string KullaniciAdi, string Sifre);
public record GirisIstegi(string KullaniciAdi, string Sifre);

public record IstasyonOlustur(string Ad, string Sehir);
public record ArizaOlustur(int IstasyonId, string Baslik, string? Aciklama, Oncelik Oncelik);
public record ArizaAta(string Kisi);
