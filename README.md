# TrackDesk – Arıza Takip API

İstasyon/tesis (gar, havalimanı, trafo merkezi vb.) bazında arıza kayıtlarını yöneten, **C# / .NET 8** ile yazılmış REST API.
Kurumsal projelerde yaygın kullanılan teknoloji yığınını gösterir: C#, ASP.NET Core, Entity Framework Core, SQL, xUnit.

## Özellikler
- İstasyon ve arıza kaydı yönetimi (CRUD'un ilgili kısımları)
- Öncelik (Düşük → Kritik) ve durum filtreli listeleme
- Durum akışı: `Açık → Atandı → Çözüldü → Kapandı` (atlama/geri dönüş engellenir, 409 döner)
- Durum bazında özet endpoint'i
- Swagger arayüzü (`/swagger`)

## Mimari
| Katman | Klasör | Sorumluluk |
|---|---|---|
| Domain | `Domain/` | Varlıklar, enum'lar, istek modelleri |
| Data | `Data/` | EF Core `DbContext`, ilişkiler, indeks |
| Servis | `Services/` | İş kuralları (test edilebilir, HTTP'den bağımsız) |
| API | `Program.cs` | Minimal API endpoint'leri, hata → HTTP kodu eşleme |

## Çalıştırma
```bash
dotnet run --project src/TrackDesk.Api
# http://localhost:5000/swagger (port launchSettings'e göre değişir)
dotnet test
```

## Örnek istekler
```bash
POST /istasyonlar        {"ad":"Ankara Garı","sehir":"Ankara"}
POST /arizalar           {"istasyonId":1,"baslik":"Sinyalizasyon arızası","oncelik":3}
POST /arizalar/1/ata     {"kisi":"Ayşe"}
POST /arizalar/1/coz
POST /arizalar/1/kapat
GET  /arizalar?durum=0&oncelik=3
GET  /arizalar/ozet
```

## Testler
xUnit ile 8 test: servis iş kuralları (SQLite in-memory) ve `WebApplicationFactory` ile uçtan uca API akışı.

## Sonraki adımlar
Kimlik doğrulama (JWT), EF Migrations, SQL Server desteği, sayfalama.

