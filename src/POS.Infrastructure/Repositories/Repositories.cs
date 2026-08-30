using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using POS.Domain.Entities;
using POS.Domain.Interfaces;
using POS.Infrastructure.Persistence;

namespace POS.Infrastructure.Repositories;

public sealed class StoreRepository(AppDbContext db) : IStoreRepository
{
    public Task<Store?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Stores.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<Store?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalised = code.Trim().ToUpperInvariant();
        return db.Stores.FirstOrDefaultAsync(s => s.Code == normalised, cancellationToken);
    }

    public async Task<IReadOnlyList<Store>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var query = db.Stores.Include(s => s.Terminals).AsQueryable();
        if (!includeInactive) query = query.Where(s => s.IsActive);

        return await query.OrderBy(s => s.Code).ToListAsync(cancellationToken);
    }

    public Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalised = code.Trim().ToUpperInvariant();
        return db.Stores.AnyAsync(s => s.Code == normalised, cancellationToken);
    }

    public async Task AddAsync(Store store, CancellationToken cancellationToken = default) =>
        await db.Stores.AddAsync(store, cancellationToken);
}

public sealed class TerminalRepository(AppDbContext db) : ITerminalRepository
{
    public Task<Terminal?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Terminals.Include(t => t.Store).FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<Terminal?> FindByUidAsync(Guid terminalUid, CancellationToken cancellationToken = default) =>
        db.Terminals.Include(t => t.Store).FirstOrDefaultAsync(t => t.TerminalUid == terminalUid, cancellationToken);

    public async Task<IReadOnlyList<Terminal>> GetByStoreAsync(Guid storeId, CancellationToken cancellationToken = default) =>
        await db.Terminals.Include(t => t.Store)
            .Where(t => t.StoreId == storeId)
            .OrderBy(t => t.CounterNumber)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Terminal>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.Terminals.Include(t => t.Store)
            .OrderBy(t => t.StoreId).ThenBy(t => t.CounterNumber)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Terminal terminal, CancellationToken cancellationToken = default) =>
        await db.Terminals.AddAsync(terminal, cancellationToken);
}

public sealed class EnrollmentCodeRepository(AppDbContext db) : IEnrollmentCodeRepository
{
    public Task<TerminalEnrollmentCode?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalised = code.Trim().ToUpperInvariant();
        return db.TerminalEnrollmentCodes.FirstOrDefaultAsync(c => c.Code == normalised, cancellationToken);
    }

    public async Task<IReadOnlyList<TerminalEnrollmentCode>> GetByStoreAsync(Guid storeId, CancellationToken cancellationToken = default) =>
        await db.TerminalEnrollmentCodes
            .Where(c => c.StoreId == storeId)
            .OrderByDescending(c => c.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalised = code.Trim().ToUpperInvariant();
        return db.TerminalEnrollmentCodes.AnyAsync(c => c.Code == normalised, cancellationToken);
    }

    public async Task AddAsync(TerminalEnrollmentCode code, CancellationToken cancellationToken = default) =>
        await db.TerminalEnrollmentCodes.AddAsync(code, cancellationToken);
}

public sealed class UnitOfWork(AppDbContext db) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);

    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        // The in-memory provider used by the integration tests supports neither transactions
        // nor execution strategies, and throws rather than no-opping. Running the operation
        // directly keeps the use cases identical under test and in production.
        if (db.Database.IsInMemory())
            return await operation(cancellationToken);

        // Npgsql is configured to retry transient faults, which serverless Postgres produces
        // routinely as it wakes and scales. That retrying strategy refuses a transaction the
        // caller opened itself, because it cannot replay one; going through the strategy is
        // what makes the transaction retriable as a single unit.
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            var result = await operation(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }
}
