using TrackDesk.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace TrackDesk.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Istasyon> Istasyonlar => Set<Istasyon>();
    public DbSet<ArizaKaydi> Arizalar => Set<ArizaKaydi>();

    public DbSet<Kullanici> Kullanicilar => Set<Kullanici>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ArizaKaydi>()
            .HasOne(a => a.Istasyon)
            .WithMany(i => i.Arizalar)
            .HasForeignKey(a => a.IstasyonId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<Kullanici>().HasIndex(k => k.KullaniciAdi).IsUnique();
        b.Entity<ArizaKaydi>().HasIndex(a => new { a.Durum, a.Oncelik });
    }
}
