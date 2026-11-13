using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TrackDesk.Tests;

public class ProxyHizSiniriApiTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly string _dbDosya = Path.Combine(Path.GetTempPath(), $"trackdesk-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> _factory;

    public ProxyHizSiniriApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Default", $"Data Source={_dbDosya}");
            b.UseSetting("Jwt:Key", "test-icin-en-az-otuz-iki-karakterlik-anahtar-0123456789");
        });
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbDosya); } catch { /* geçici dosya */ }
    }

    private HttpClient Istemci(bool guvenilirBaslik) =>
        _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("RateLimit:GirisDakika", "2");
            b.UseSetting("Proxy:GuvenilirBaslik", guvenilirBaslik ? "true" : "false");
        }).CreateClient();

    private static async Task<HttpStatusCode> Giris(HttpClient c, string xForwardedFor)
    {
        var istek = new HttpRequestMessage(HttpMethod.Post, "/auth/giris")
        {
            Content = JsonContent.Create(new { kullaniciAdi = "x", sifre = "yanlis" })
        };
        istek.Headers.Add("X-Forwarded-For", xForwardedFor);
        return (await c.SendAsync(istek)).StatusCode;
    }

    [Fact]
    public async Task Kapaliyken_FarkliXForwardedFor_AyniKovayaDuser()
    {
        var c = Istemci(guvenilirBaslik: false);

        var kodlar = new List<HttpStatusCode>();
        foreach (var ip in new[] { "1.1.1.1", "2.2.2.2", "3.3.3.3" })
            kodlar.Add(await Giris(c, ip));

        Assert.Equal(HttpStatusCode.TooManyRequests, kodlar[2]);
    }

    [Fact]
    public async Task Acikken_FarkliXForwardedFor_AyriKovalaraDuser()
    {
        var c = Istemci(guvenilirBaslik: true);

        var kodlar = new List<HttpStatusCode>();
        foreach (var ip in new[] { "1.1.1.1", "2.2.2.2", "3.3.3.3" })
            kodlar.Add(await Giris(c, ip));

        Assert.All(kodlar, k => Assert.Equal(HttpStatusCode.Unauthorized, k));
    }

    [Fact]
    public async Task Acikken_AyniIstemcininDenemeleriYineSiniranir()
    {
        var c = Istemci(guvenilirBaslik: true);

        var kodlar = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
            kodlar.Add(await Giris(c, "9.9.9.9"));

        Assert.Equal(HttpStatusCode.TooManyRequests, kodlar[2]);
    }
}
