using KamuOps.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace KamuOps.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Istasyon> Istasyonlar => Set<Istasyon>();
    public DbSet<ArizaKaydi> Arizalar => Set<ArizaKaydi>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ArizaKaydi>()
            .HasOne(a => a.Istasyon)
            .WithMany(i => i.Arizalar)
            .HasForeignKey(a => a.IstasyonId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<ArizaKaydi>().HasIndex(a => new { a.Durum, a.Oncelik });
    }
}
