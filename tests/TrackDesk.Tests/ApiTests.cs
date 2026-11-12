using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TrackDesk.Tests;

public class ApiTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly string _dbDosya = Path.Combine(Path.GetTempPath(), $"TrackDesk-{Guid.NewGuid():N}.db");
    private readonly HttpClient _client;

    public ApiTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(b =>
            b.UseSetting("ConnectionStrings:Default", $"Data Source={_dbDosya}")).CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbDosya); } catch { /* geçici dosya */ }
    }

    [Fact]
    public async Task UctanUca_IstasyonVeAriza_AkisCalisir()
    {
        var ist = await _client.PostAsJsonAsync("/istasyonlar", new { ad = "Esenboğa", sehir = "Ankara" });
        Assert.Equal(HttpStatusCode.Created, ist.StatusCode);

        var ar = await _client.PostAsJsonAsync("/arizalar",
            new { istasyonId = 1, baslik = "Pist ışıkları", aciklama = "Sönük", oncelik = 3 });
        Assert.Equal(HttpStatusCode.Created, ar.StatusCode);

        var kapat = await _client.PostAsync("/arizalar/1/kapat", null);
        Assert.Equal(HttpStatusCode.Conflict, kapat.StatusCode); // çözülmeden kapanamaz
    }

    [Fact]
    public async Task OlmayanKayit_404Doner()
    {
        var r = await _client.PostAsync("/arizalar/999/coz", null);
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }
}
