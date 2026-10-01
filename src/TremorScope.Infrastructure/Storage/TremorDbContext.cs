using Microsoft.EntityFrameworkCore;

namespace TremorScope.Infrastructure.Storage;

public sealed class TremorDbContext(DbContextOptions<TremorDbContext> options) : DbContext(options)
{
    /// <summary>指定したファイルの SQLite データベースを開く</summary>
    public static TremorDbContext Open(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = databasePath, ForeignKeys = true, Pooling = false };
        return new TremorDbContext(new DbContextOptionsBuilder<TremorDbContext>().UseSqlite(builder.ToString()).Options);
    }

    public DbSet<PatientEntity> Patients => Set<PatientEntity>();

    public DbSet<SessionEntity> Sessions => Set<SessionEntity>();

    public DbSet<RecordingEntity> Recordings => Set<RecordingEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.Entity<PatientEntity>(e =>
        {
            e.HasIndex(p => p.LocalId).IsUnique();
            e.HasIndex(p => p.PseudonymId).IsUnique();
        });
        modelBuilder.Entity<SessionEntity>(e =>
        {
            // 患者を削除したら、測定もすべて削除する
            e.HasOne(s => s.Patient).WithMany(p => p.Sessions).HasForeignKey(s => s.PatientId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(s => new { s.PatientId, s.MeasuredAtUtc });
        });
        modelBuilder.Entity<RecordingEntity>(e =>
        {
            e.HasOne(r => r.Session).WithMany(s => s.Recordings).HasForeignKey(r => r.SessionId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
