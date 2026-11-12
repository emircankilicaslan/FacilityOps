using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using TrackDesk.Api.Data;
using TrackDesk.Api.Domain;
using TrackDesk.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=TrackDesk.db"));
builder.Services.AddScoped<ArizaService>();
builder.Services.AddScoped<AuthService>();

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
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();

// İş kuralı hatalarını uygun HTTP koduna çevir.
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (KeyNotFoundException e) { await Hata(ctx, 404, e.Message); }
    catch (UnauthorizedAccessException e) { await Hata(ctx, 401, e.Message); }
    catch (ArgumentException e) { await Hata(ctx, 400, e.Message); }
    catch (InvalidOperationException e) { await Hata(ctx, 409, e.Message); }
});

app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/auth/kayit", async (KayitIstegi istek, AuthService s) =>
{
    var k = await s.KayitAsync(istek);
    return Results.Created($"/auth/kayit/{k.Id}", new { k.Id, k.KullaniciAdi, k.Rol });
});
app.MapPost("/auth/giris", async (GirisIstegi istek, AuthService s) => new { token = await s.GirisAsync(istek) });

var korumali = app.MapGroup("").RequireAuthorization();

korumali.MapPost("/istasyonlar", async (IstasyonOlustur istek, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(istek.Ad) || string.IsNullOrWhiteSpace(istek.Sehir))
        return Results.BadRequest(new { hata = "Ad ve şehir zorunludur." });
    var i = new Istasyon { Ad = istek.Ad.Trim(), Sehir = istek.Sehir.Trim() };
    db.Istasyonlar.Add(i);
    await db.SaveChangesAsync();
    return Results.Created($"/istasyonlar/{i.Id}", i);
});

korumali.MapGet("/istasyonlar", (AppDbContext db) => db.Istasyonlar.AsNoTracking().OrderBy(i => i.Ad).ToListAsync());

korumali.MapPost("/arizalar", async (ArizaOlustur istek, ArizaService s) =>
{
    var a = await s.OlusturAsync(istek);
    return Results.Created($"/arizalar/{a.Id}", a);
});

korumali.MapGet("/arizalar", (ArizaDurumu? durum, Oncelik? oncelik, ArizaService s) => s.ListeleAsync(durum, oncelik));
korumali.MapGet("/arizalar/ozet", (ArizaService s) => s.OzetAsync());
korumali.MapPost("/arizalar/{id:int}/ata", (int id, ArizaAta istek, ArizaService s) => s.AtaAsync(id, istek.Kisi));
korumali.MapPost("/arizalar/{id:int}/coz", (int id, ArizaService s) => s.CozAsync(id));
korumali.MapPost("/arizalar/{id:int}/kapat", (int id, ArizaService s) => s.KapatAsync(id)).RequireAuthorization("YoneticiPolitikasi");

app.Run();

static Task Hata(HttpContext ctx, int kod, string mesaj)
{
    ctx.Response.StatusCode = kod;
    return ctx.Response.WriteAsJsonAsync(new { hata = mesaj });
}

public partial class Program;
