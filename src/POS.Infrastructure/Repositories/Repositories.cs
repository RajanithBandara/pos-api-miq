using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using POS.Domain.Entities;
using POS.Domain.Interfaces;
using POS.Infrastructure.Persistence;

namespace POS.Infrastructure.Repositories;

public class SaleRepository : GenericRepository<Sale, Guid>, ISaleRepository
{
    public SaleRepository(AppDbContext context) : base(context) { }

    public async Task<Sale?> GetWithDetailsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(s => s.Items)
            .Include(s => s.Payments)
            .Include(s => s.CashierEmployee)
            .Include(s => s.Customer)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<Sale?> GetByIdempotencyKeyAsync(Guid storeId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(s => s.Items)
            .Include(s => s.Payments)
            .Include(s => s.CashierEmployee)
            .Include(s => s.Customer)
            .FirstOrDefaultAsync(s => s.StoreId == storeId && s.IdempotencyKey == idempotencyKey, cancellationToken);
    }

    public async Task<string> GenerateNextInvoiceNumberAsync(Guid storeId, Guid terminalId, CancellationToken cancellationToken = default)
    {
        var todayStr = DateTime.UtcNow.ToString("yyyyMMdd");
        var countToday = await DbSet
            .CountAsync(s => s.StoreId == storeId && s.CompletedAtUtc.Date == DateTime.UtcNow.Date, cancellationToken);

        var termPrefix = terminalId.ToString()[..4].ToUpperInvariant();
        return $"INV-{todayStr}-{termPrefix}-{(countToday + 1):D5}";
    }

    public async Task<IReadOnlyList<Sale>> GetSalesByDateRangeAsync(Guid storeId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(s => s.Items)
            .Include(s => s.Payments)
            .Include(s => s.CashierEmployee)
            .Include(s => s.Customer)
            .Where(s => s.StoreId == storeId && s.CompletedAtUtc >= fromUtc && s.CompletedAtUtc <= toUtc)
            .OrderByDescending(s => s.CompletedAtUtc)
            .ToListAsync(cancellationToken);
    }
}

public class ProductRepository : GenericRepository<Product, Guid>, IProductRepository
{
    public ProductRepository(AppDbContext context) : base(context) { }

    public async Task<Product?> GetWithDetailsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(p => p.Category)
            .Include(p => p.Barcodes)
            .Include(p => p.Stocks)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<Product?> GetBySkuAsync(string sku, Guid? storeId = null, CancellationToken cancellationToken = default)
    {
        var query = DbSet
            .Include(p => p.Category)
            .Include(p => p.Barcodes)
            .Include(p => p.Stocks)
            .Where(p => p.Sku.ToLower() == sku.ToLower());

        if (storeId.HasValue)
        {
            query = query.Where(p => p.StoreId == null || p.StoreId == storeId.Value);
        }

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Product?> GetByBarcodeAsync(string barcode, Guid? storeId = null, CancellationToken cancellationToken = default)
    {
        var query = DbSet
            .Include(p => p.Category)
            .Include(p => p.Barcodes)
            .Include(p => p.Stocks)
            .Where(p => p.Barcodes.Any(b => b.Barcode == barcode));

        if (storeId.HasValue)
        {
            query = query.Where(p => p.StoreId == null || p.StoreId == storeId.Value);
        }

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Product>> GetByCategoryIdAsync(Guid categoryId, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(p => p.Barcodes)
            .Include(p => p.Stocks)
            .Where(p => p.CategoryId == categoryId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Product>> GetLowStockProductsAsync(Guid storeId, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(p => p.Barcodes)
            .Include(p => p.Stocks.Where(s => s.StoreId == storeId))
            .Where(p => p.TrackInventory && p.Stocks.Any(s => s.StoreId == storeId && s.QuantityOnHand <= p.LowStockThreshold))
            .ToListAsync(cancellationToken);
    }
}

public class StockRepository : GenericRepository<Stock, Guid>, IStockRepository
{
    public StockRepository(AppDbContext context) : base(context) { }

    public async Task<Stock?> GetByProductAndStoreAsync(Guid productId, Guid storeId, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .FirstOrDefaultAsync(s => s.ProductId == productId && s.StoreId == storeId, cancellationToken);
    }

    public async Task<IReadOnlyList<StockBatch>> GetAvailableBatchesAsync(Guid productId, Guid storeId, CancellationToken cancellationToken = default)
    {
        return await Context.StockBatches
            .Where(b => b.ProductId == productId && b.StoreId == storeId && b.IsActive && b.CurrentQuantity > 0)
            .OrderBy(b => b.ExpiryDateUtc)
            .ThenBy(b => b.ReceivedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StockBatch>> GetExpiringBatchesAsync(Guid storeId, DateTime expiryThresholdUtc, CancellationToken cancellationToken = default)
    {
        return await Context.StockBatches
            .Where(b => b.StoreId == storeId && b.IsActive && b.CurrentQuantity > 0 && b.ExpiryDateUtc.HasValue && b.ExpiryDateUtc.Value <= expiryThresholdUtc)
            .OrderBy(b => b.ExpiryDateUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task AddBatchAsync(StockBatch batch, CancellationToken cancellationToken = default)
    {
        await Context.StockBatches.AddAsync(batch, cancellationToken);
    }

    public async Task AddMovementAsync(StockMovement movement, CancellationToken cancellationToken = default)
    {
        await Context.StockMovements.AddAsync(movement, cancellationToken);
    }

    public async Task<IReadOnlyList<StockMovement>> GetMovementsByProductAsync(Guid productId, Guid storeId, int limit = 50, CancellationToken cancellationToken = default)
    {
        return await Context.StockMovements
            .Include(m => m.StockBatch)
            .Where(m => m.ProductId == productId && m.StoreId == storeId)
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}

public class SyncRepository : ISyncRepository
{
    private readonly AppDbContext _context;

    public SyncRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<SyncIdempotencyRecord?> GetIdempotencyRecordAsync(Guid terminalId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        return await _context.SyncIdempotencyRecords
            .FirstOrDefaultAsync(r => r.PosTerminalId == terminalId && r.IdempotencyKey == idempotencyKey, cancellationToken);
    }

    public async Task SaveIdempotencyRecordAsync(SyncIdempotencyRecord record, CancellationToken cancellationToken = default)
    {
        await _context.SyncIdempotencyRecords.AddAsync(record, cancellationToken);
    }

    public async Task<long> GetLatestVersionAsync(Guid storeId, CancellationToken cancellationToken = default)
    {
        var maxVersion = await _context.SyncChangeLogs
            .Where(c => c.StoreId == storeId)
            .MaxAsync(c => (long?)c.Version, cancellationToken);

        return maxVersion ?? 0;
    }

    public async Task<IReadOnlyList<SyncChangeLog>> GetChangesSinceVersionAsync(Guid storeId, long sinceVersion, int limit = 500, CancellationToken cancellationToken = default)
    {
        return await _context.SyncChangeLogs
            .Where(c => c.StoreId == storeId && c.Version > sinceVersion)
            .OrderBy(c => c.Version)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task LogChangeAsync(SyncChangeLog changeLog, CancellationToken cancellationToken = default)
    {
        var latestVersion = await GetLatestVersionAsync(changeLog.StoreId, cancellationToken);
        changeLog.Version = latestVersion + 1;
        await _context.SyncChangeLogs.AddAsync(changeLog, cancellationToken);
    }

    public async Task LogChangesBatchAsync(IEnumerable<SyncChangeLog> changeLogs, CancellationToken cancellationToken = default)
    {
        var list = changeLogs.ToList();
        if (list.Count == 0) return;

        var storeId = list[0].StoreId;
        var currentVersion = await GetLatestVersionAsync(storeId, cancellationToken);

        foreach (var item in list)
        {
            currentVersion++;
            item.Version = currentVersion;
        }

        await _context.SyncChangeLogs.AddRangeAsync(list, cancellationToken);
    }
}

public class UserRepository : GenericRepository<User, Guid>, IUserRepository
{
    public UserRepository(AppDbContext context) : base(context) { }

    public async Task<User?> GetByUsernameWithRolesAsync(string username, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower(), cancellationToken);
    }

    public async Task<User?> GetByEmailWithRolesAsync(string email, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower(), cancellationToken);
    }

    public async Task<User?> GetWithRolesAndPermissionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                    .ThenInclude(r => r!.RolePermissions)
                        .ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
    }

    public async Task<RefreshToken?> GetRefreshTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        return await Context.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == token, cancellationToken);
    }

    public async Task AddRefreshTokenAsync(RefreshToken refreshToken, CancellationToken cancellationToken = default)
    {
        await Context.RefreshTokens.AddAsync(refreshToken, cancellationToken);
    }

    public void UpdateRefreshToken(RefreshToken refreshToken)
    {
        Context.RefreshTokens.Update(refreshToken);
    }
}

public class EmployeeRepository : GenericRepository<Employee, Guid>, IEmployeeRepository
{
    public EmployeeRepository(AppDbContext context) : base(context) { }

    public async Task<Employee?> GetByCodeAsync(Guid storeId, string employeeCode, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(e => e.Store)
            .Include(e => e.User)
            .FirstOrDefaultAsync(e => e.StoreId == storeId && e.EmployeeCode.ToLower() == employeeCode.ToLower(), cancellationToken);
    }

    public async Task<IReadOnlyList<Employee>> GetByStoreAsync(Guid storeId, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(e => e.Store)
            .Include(e => e.User)
            .Where(e => e.StoreId == storeId)
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .ToListAsync(cancellationToken);
    }
}

public class CustomerRepository : GenericRepository<Customer, Guid>, ICustomerRepository
{
    public CustomerRepository(AppDbContext context) : base(context) { }

    public async Task<Customer?> GetByPhoneOrEmailAsync(string? phone, string? email, Guid? storeId = null, CancellationToken cancellationToken = default)
    {
        var query = DbSet.AsQueryable();
        if (storeId.HasValue)
        {
            query = query.Where(c => c.StoreId == null || c.StoreId == storeId.Value);
        }

        if (!string.IsNullOrWhiteSpace(phone) && !string.IsNullOrWhiteSpace(email))
        {
            return await query.FirstOrDefaultAsync(c => c.Phone == phone || c.Email == email, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(phone))
        {
            return await query.FirstOrDefaultAsync(c => c.Phone == phone, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            return await query.FirstOrDefaultAsync(c => c.Email == email, cancellationToken);
        }

        return null;
    }

    public async Task<IReadOnlyList<Customer>> SearchCustomersAsync(string query, Guid? storeId = null, int limit = 20, CancellationToken cancellationToken = default)
    {
        var q = DbSet.AsQueryable();
        if (storeId.HasValue)
        {
            q = q.Where(c => c.StoreId == null || c.StoreId == storeId.Value);
        }

        var s = query.ToLower().Trim();
        return await q
            .Where(c => c.FirstName.ToLower().Contains(s) ||
                        c.LastName.ToLower().Contains(s) ||
                        (c.Phone != null && c.Phone.Contains(s)) ||
                        (c.Email != null && c.Email.ToLower().Contains(s)))
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
