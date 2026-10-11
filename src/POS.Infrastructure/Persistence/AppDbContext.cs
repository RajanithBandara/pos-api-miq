using Microsoft.EntityFrameworkCore;
using POS.Domain.Entities;

namespace POS.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Store> Stores => Set<Store>();
    public DbSet<Terminal> Terminals => Set<Terminal>();
    public DbSet<SyncEvent> SyncEvents => Set<SyncEvent>();
    public DbSet<ProductKey> ProductKeys => Set<ProductKey>();

    /// <summary>Who may use the web dashboard. See DashboardUser for why this is ours and not Neon's.</summary>
    public DbSet<DashboardUser> DashboardUsers => Set<DashboardUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
