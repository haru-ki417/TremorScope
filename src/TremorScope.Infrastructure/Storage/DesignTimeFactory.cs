using Microsoft.EntityFrameworkCore.Design;

namespace TremorScope.Infrastructure.Storage;

/// <summary>データベースの変更手順（マイグレーション）を作るときだけ使う</summary>
internal sealed class DesignTimeFactory : IDesignTimeDbContextFactory<TremorDbContext>
{
    public TremorDbContext CreateDbContext(string[] args) => TremorDbContext.Open("design-time.db");
}
