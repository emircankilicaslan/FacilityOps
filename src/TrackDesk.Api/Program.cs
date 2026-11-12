using TrackDesk.Api.Data;
using TrackDesk.Api.Domain;
using TrackDesk.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=TrackDesk.db"));
builder.Services.AddScoped<ArizaService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();

// İş kuralı hatalarını uygun HTTP koduna çevir.
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (KeyNotFoundException e) { await Hata(ctx, 404, e.Message); }
    catch (ArgumentException e) { await Hata(ctx, 400, e.Message); }
    catch (InvalidOperationException e) { await Hata(ctx, 409, e.Message); }
});

app.UseSwagger();
app.UseSwaggerUI();

app.MapPost("/istasyonlar", async (IstasyonOlustur istek, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(istek.Ad) || string.IsNullOrWhiteSpace(istek.Sehir))
        return Results.BadRequest(new { hata = "Ad ve şehir zorunludur." });
    var i = new Istasyon { Ad = istek.Ad.Trim(), Sehir = istek.Sehir.Trim() };
    db.Istasyonlar.Add(i);
    await db.SaveChangesAsync();
    return Results.Created($"/istasyonlar/{i.Id}", i);
});

app.MapGet("/istasyonlar", (AppDbContext db) => db.Istasyonlar.AsNoTracking().OrderBy(i => i.Ad).ToListAsync());

app.MapPost("/arizalar", async (ArizaOlustur istek, ArizaService s) =>
{
    var a = await s.OlusturAsync(istek);
    return Results.Created($"/arizalar/{a.Id}", a);
});

app.MapGet("/arizalar", (ArizaDurumu? durum, Oncelik? oncelik, ArizaService s) => s.ListeleAsync(durum, oncelik));
app.MapGet("/arizalar/ozet", (ArizaService s) => s.OzetAsync());
app.MapPost("/arizalar/{id:int}/ata", (int id, ArizaAta istek, ArizaService s) => s.AtaAsync(id, istek.Kisi));
app.MapPost("/arizalar/{id:int}/coz", (int id, ArizaService s) => s.CozAsync(id));
app.MapPost("/arizalar/{id:int}/kapat", (int id, ArizaService s) => s.KapatAsync(id));

app.Run();

static Task Hata(HttpContext ctx, int kod, string mesaj)
{
    ctx.Response.StatusCode = kod;
    return ctx.Response.WriteAsJsonAsync(new { hata = mesaj });
}

public partial class Program;
