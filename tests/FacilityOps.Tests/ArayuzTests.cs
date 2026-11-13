using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FacilityOps.Tests;

/// <summary>wwwroot altındaki web arayüzünün sunumu ve güvenlik başlıkları.</summary>
public class ArayuzTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly string _dbDosya = Path.Combine(Path.GetTempPath(), $"facilityops-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> _factory;

    public ArayuzTests(WebApplicationFactory<Program> factory)
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

    [Theory]
    [InlineData("/")]
    [InlineData("/index.html")]
    [InlineData("/app.js")]
    [InlineData("/app.css")]
    public async Task ArayuzDosyalari_KimlikDogrulamasiz_Sunulur(string yol)
    {
        var r = await _factory.CreateClient().GetAsync(yol);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Arayuz_IcerikGuvenlikPolitikasiIleSunulur_SatirIciBetikIzinVerilmez()
    {
        var r = await _factory.CreateClient().GetAsync("/");
        var csp = string.Join(";", r.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("default-src 'self'", csp);
        Assert.DoesNotContain("unsafe-inline", csp);

        var html = await r.Content.ReadAsStringAsync();
        // Satır içi betik/stil yok: CSP bunları engellerdi.
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain(" style=", html);
        Assert.DoesNotContain(" onclick=", html);
    }

    [Fact]
    public async Task Swagger_KendiSayfalariIcinCspUygulanmaz()
    {
        var r = await _factory.CreateClient().GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.False(r.Headers.Contains("Content-Security-Policy"));
    }

    [Fact]
    public async Task KorunanApiUcNoktalari_ArayuzEklendiktenSonraDaKorunur()
    {
        var r = await _factory.CreateClient().GetAsync("/arizalar");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }
}
