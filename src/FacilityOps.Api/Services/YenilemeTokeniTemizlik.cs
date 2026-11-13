using Microsoft.EntityFrameworkCore;
using FacilityOps.Api.Data;

namespace FacilityOps.Api.Services;

/// <summary>
/// Süresi dolmuş yenileme tokenı kayıtlarını siler. Yalnızca <b>süresi dolmuş</b> kayıtlar silinir:
/// iptal edilmiş ama süresi dolmamış bir token, tekrar kullanım (çalınma) tespiti için tabloda kalmalıdır;
/// silinirse saldırgan bir "tekrar kullanım" yerine sıradan "geçersiz token" hatası alır ve oturumlar kapatılmaz.
/// </summary>
public static class YenilemeTokeniTemizleyici
{
    /// <returns>Silinen kayıt sayısı.</returns>
    public static Task<int> TemizleAsync(AppDbContext db, DateTime simdi, TimeSpan saklama, CancellationToken iptal = default)
    {
        var sinir = simdi - saklama;
        return db.YenilemeTokenleri.Where(t => t.SonKullanma < sinir).ExecuteDeleteAsync(iptal);
    }
}

/// <summary>
/// Temizliği düzenli aralıklarla çalıştırır. Aralık <c>Jwt:TemizlikDakika</c> ile ayarlanır (varsayılan 60);
/// 0 ya da negatif değer temizliği kapatır. Süresi dolan kayıtlar <c>Jwt:TemizlikSaklamaSaat</c> (varsayılan 24) kadar saklanır.
/// </summary>
public class YenilemeTokeniTemizlikServisi(IServiceScopeFactory kapsamFabrikasi, IConfiguration config, ILogger<YenilemeTokeniTemizlikServisi> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken durdurma)
    {
        var dakika = config.GetValue("Jwt:TemizlikDakika", 60);
        if (dakika <= 0)
        {
            log.LogInformation("Yenileme tokenı temizliği kapalı (Jwt:TemizlikDakika <= 0).");
            return;
        }

        var saklama = TimeSpan.FromHours(config.GetValue("Jwt:TemizlikSaklamaSaat", 24));
        using var zamanlayici = new PeriodicTimer(TimeSpan.FromMinutes(dakika));
        try
        {
            while (await zamanlayici.WaitForNextTickAsync(durdurma))
            {
                try
                {
                    await using var kapsam = kapsamFabrikasi.CreateAsyncScope();
                    var db = kapsam.ServiceProvider.GetRequiredService<AppDbContext>();
                    var silinen = await YenilemeTokeniTemizleyici.TemizleAsync(db, DateTime.UtcNow, saklama, durdurma);
                    if (silinen > 0) log.LogInformation("Süresi dolmuş {Adet} yenileme tokenı silindi.", silinen);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    log.LogError(e, "Yenileme tokenı temizliği başarısız oldu; bir sonraki turda yeniden denenecek.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Uygulama kapanıyor.
        }
    }
}
