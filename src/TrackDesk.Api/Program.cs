using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using TrackDesk.Api.Data;
using TrackDesk.Api.Domain;
using TrackDesk.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=trackdesk.db"));
builder.Services.AddScoped<ArizaService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("veritabani");

var jwtAnahtar = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtAnahtar) || jwtAnahtar.Length < 32)
    throw new InvalidOperationException("Jwt:Key ayarlanmalı (en az 32 karakter). Ortam değişkeni: Jwt__Key");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidateAudience = true,
        ValidAudience = builder.Configuration["Jwt:Audience"],
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtAnahtar)),
        ClockSkew = TimeSpan.FromMinutes(1)
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("YoneticiPolitikasi", p => p.RequireRole(Roller.Yonetici));

// Kaba kuvvet denemelerine karşı: kayıt/giriş uçlarında IP başına dakikada sınırlı istek.
var girisLimiti = builder.Configuration.GetValue("RateLimit:GirisDakika", 10);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("giris", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "bilinmeyen",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = girisLimiti, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT",
        Description = "/auth/giris ile alınan token"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = []
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();

// Güvenlik başlıkları
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});

// Hataları RFC 7807 (ProblemDetails) biçiminde ve uygun HTTP koduyla döndür; beklenmeyen hatalar loglanır, ayrıntı sızdırılmaz.
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (KeyNotFoundException e) { await Hata(ctx, 404, "Bulunamadı", e.Message); }
    catch (UnauthorizedAccessException e) { await Hata(ctx, 401, "Yetkisiz", e.Message); }
    catch (ArgumentException e) { await Hata(ctx, 400, "Geçersiz istek", e.Message); }
    catch (InvalidOperationException e) { await Hata(ctx, 409, "Çakışma", e.Message); }
    catch (Exception e)
    {
        ctx.RequestServices.GetRequiredService<ILogger<Program>>().LogError(e, "Beklenmeyen hata: {Yol}", ctx.Request.Path);
        await Hata(ctx, 500, "Sunucu hatası", "Beklenmeyen bir hata oluştu.");
    }
});

app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthChecks("/saglik");

app.MapPost("/auth/kayit", async (KayitIstegi istek, AuthService s) =>
{
    var k = await s.KayitAsync(istek);
    return Results.Created($"/auth/kayit/{k.Id}", new { k.Id, k.KullaniciAdi, k.Rol });
}).RequireRateLimiting("giris");

app.MapPost("/auth/giris", async (GirisIstegi istek, AuthService s) => new { token = await s.GirisAsync(istek) })
    .RequireRateLimiting("giris");

var korumali = app.MapGroup("").RequireAuthorization();

korumali.MapPost("/istasyonlar", async (IstasyonOlustur istek, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(istek.Ad) || string.IsNullOrWhiteSpace(istek.Sehir))
        throw new ArgumentException("Ad ve şehir zorunludur.");
    if (istek.Ad.Length > 100 || istek.Sehir.Length > 50)
        throw new ArgumentException("Ad en fazla 100, şehir en fazla 50 karakter olabilir.");
    var i = new Istasyon { Ad = istek.Ad.Trim(), Sehir = istek.Sehir.Trim() };
    db.Istasyonlar.Add(i);
    await db.SaveChangesAsync();
    return Results.Created($"/istasyonlar/{i.Id}", i);
});

korumali.MapGet("/istasyonlar", (AppDbContext db) => db.Istasyonlar.AsNoTracking().OrderBy(i => i.Ad).ToListAsync());

korumali.MapPost("/arizalar", async (ArizaOlustur istek, ArizaService s, ClaimsPrincipal u) =>
{
    var a = await s.OlusturAsync(istek, u.Identity?.Name ?? "");
    return Results.Created($"/arizalar/{a.Id}", a);
});

korumali.MapGet("/arizalar", (ArizaDurumu? durum, Oncelik? oncelik, bool? gecikmis, int? sayfa, int? boyut, string? atanan, string? ara, ArizaService s) =>
    s.ListeleAsync(durum, oncelik, gecikmis, sayfa ?? 1, boyut ?? 20, atanan, ara));

korumali.MapGet("/arizalar/ozet", (ArizaService s) => s.OzetAsync());
korumali.MapGet("/arizalar/{id:int}/gecmis", (int id, ArizaService s) => s.GecmisAsync(id));
korumali.MapGet("/arizalar/{id:int}", (int id, ArizaService s) => s.DetayAsync(id));

korumali.MapGet("/arizalar/disa-aktar", async (ArizaDurumu? durum, Oncelik? oncelik, bool? gecikmis, ArizaService s) =>
        Results.File(await s.CsvAsync(durum, oncelik, gecikmis), "text/csv; charset=utf-8", "arizalar.csv"))
    .RequireAuthorization("YoneticiPolitikasi");

korumali.MapPost("/arizalar/{id:int}/ata", (int id, ArizaAta istek, ArizaService s, ClaimsPrincipal u) =>
    s.AtaAsync(id, istek.Kisi, u.Identity?.Name ?? ""));
korumali.MapPost("/arizalar/{id:int}/coz", (int id, ArizaService s, ClaimsPrincipal u) =>
    s.CozAsync(id, u.Identity?.Name ?? ""));
korumali.MapPost("/arizalar/{id:int}/kapat", (int id, ArizaService s, ClaimsPrincipal u) =>
        s.KapatAsync(id, u.Identity?.Name ?? ""))
    .RequireAuthorization("YoneticiPolitikasi");

app.Run();

static Task Hata(HttpContext ctx, int kod, string baslik, string detay)
{
    ctx.Response.StatusCode = kod;
    return ctx.Response.WriteAsJsonAsync(
        new ProblemDetails { Status = kod, Title = baslik, Detail = detay, Instance = ctx.Request.Path },
        options: null, contentType: "application/problem+json");
}

public partial class Program;
