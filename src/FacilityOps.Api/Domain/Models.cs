using System.ComponentModel.DataAnnotations;

namespace FacilityOps.Api.Domain;

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

    /// <summary>Önceliğe göre hesaplanan hedef çözüm zamanı (SLA).</summary>
    public DateTime SlaSonTarih { get; set; }

    public DateTime? KapanmaZamani { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool Gecikmis => Durum != ArizaDurumu.Kapandi && DateTime.UtcNow > SlaSonTarih;
}

/// <summary>Bir arıza üzerindeki her işlemin denetim kaydı (kim, ne zaman, hangi durumdan hangisine).</summary>
public class ArizaGecmisi
{
    public int Id { get; set; }
    public int ArizaId { get; set; }

    [MaxLength(50)]
    public string Islem { get; set; } = "";

    public ArizaDurumu? EskiDurum { get; set; }
    public ArizaDurumu YeniDurum { get; set; }

    [MaxLength(100)]
    public string Kullanici { get; set; } = "";

    public DateTime Zaman { get; set; } = DateTime.UtcNow;
}

public record Sayfali<T>(int Sayfa, int Boyut, int Toplam, List<T> Ogeler)
{
    public int ToplamSayfa => Boyut == 0 ? 0 : (int)Math.Ceiling(Toplam / (double)Boyut);
}

public record ArizaOzeti(Dictionary<string, int> Durumlar, int Gecikmis);

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
