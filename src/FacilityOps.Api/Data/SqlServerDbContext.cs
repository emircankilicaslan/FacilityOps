using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FacilityOps.Api.Data;

/// <summary>
/// SQL Server için bağlam. Model <see cref="AppDbContext"/> ile aynıdır; yalnızca migration kümesi ayrıdır:
/// EF Core migration'ları sağlayıcıya özgü olduğundan SQL Server betikleri <c>Data/MigrationsSqlServer</c> altında,
/// SQLite betikleri <c>Data/Migrations</c> altında tutulur ve her sağlayıcı yalnızca kendi kümesini uygular.
/// Servisler <see cref="AppDbContext"/>'e bağımlı kalır; <c>Database:Provider=SqlServer</c> iken bu tür enjekte edilir.
/// </summary>
public class SqlServerDbContext(DbContextOptions<SqlServerDbContext> options) : AppDbContext(options);

/// <summary>Yalnızca <c>dotnet ef migrations</c> için (tasarım zamanı); bağlantı kurulmaz.</summary>
public class SqlServerDbContextFactory : IDesignTimeDbContextFactory<SqlServerDbContext>
{
    public SqlServerDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SqlServerDbContext>()
            .UseSqlServer("Server=localhost;Database=FacilityOps;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);
}
