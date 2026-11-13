using FacilityOps.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FacilityOps.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Istasyon> Istasyonlar => Set<Istasyon>();
    public DbSet<ArizaKaydi> Arizalar => Set<ArizaKaydi>();

    public DbSet<Kullanici> Kullanicilar => Set<Kullanici>();
    public DbSet<YenilemeTokeni> YenilemeTokenleri => Set<YenilemeTokeni>();
    public DbSet<ArizaGecmisi> ArizaGecmisleri => Set<ArizaGecmisi>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ArizaKaydi>()
            .HasOne(a => a.Istasyon)
            .WithMany(i => i.Arizalar)
            .HasForeignKey(a => a.IstasyonId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<ArizaGecmisi>()
            .HasOne<ArizaKaydi>().WithMany().HasForeignKey(g => g.ArizaId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ArizaGecmisi>().HasIndex(g => g.ArizaId);
        b.Entity<ArizaKaydi>().HasIndex(a => a.SlaSonTarih);
        b.Entity<Kullanici>().HasIndex(k => k.KullaniciAdi).IsUnique();
        b.Entity<YenilemeTokeni>().HasIndex(t => t.TokenOzeti).IsUnique();
        b.Entity<YenilemeTokeni>()
            .HasOne(t => t.Kullanici).WithMany().HasForeignKey(t => t.KullaniciId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ArizaKaydi>().HasIndex(a => new { a.Durum, a.Oncelik });
    }
}
