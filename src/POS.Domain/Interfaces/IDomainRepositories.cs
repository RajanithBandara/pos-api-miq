using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using POS.Domain.Entities;
using POS.Domain.Enums;

namespace POS.Domain.Interfaces;

public interface ISaleRepository : IRepository<Sale, Guid>
{
    Task<Sale?> GetWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Sale?> GetByIdempotencyKeyAsync(Guid storeId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<string> GenerateNextInvoiceNumberAsync(Guid storeId, Guid terminalId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Sale>> GetSalesByDateRangeAsync(Guid storeId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);
}

public interface IProductRepository : IRepository<Product, Guid>
{
    Task<Product?> GetWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Product?> GetBySkuAsync(string sku, Guid? storeId = null, CancellationToken cancellationToken = default);
    Task<Product?> GetByBarcodeAsync(string barcode, Guid? storeId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Product>> GetByCategoryIdAsync(Guid categoryId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Product>> GetLowStockProductsAsync(Guid storeId, CancellationToken cancellationToken = default);
}

public interface IStockRepository : IRepository<Stock, Guid>
{
    Task<Stock?> GetByProductAndStoreAsync(Guid productId, Guid storeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockBatch>> GetAvailableBatchesAsync(Guid productId, Guid storeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockBatch>> GetExpiringBatchesAsync(Guid storeId, DateTime expiryThresholdUtc, CancellationToken cancellationToken = default);
    Task AddBatchAsync(StockBatch batch, CancellationToken cancellationToken = default);
    Task AddMovementAsync(StockMovement movement, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockMovement>> GetMovementsByProductAsync(Guid productId, Guid storeId, int limit = 50, CancellationToken cancellationToken = default);
}

public interface ISyncRepository
{
    Task<SyncIdempotencyRecord?> GetIdempotencyRecordAsync(Guid terminalId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task SaveIdempotencyRecordAsync(SyncIdempotencyRecord record, CancellationToken cancellationToken = default);
    Task<long> GetLatestVersionAsync(Guid storeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SyncChangeLog>> GetChangesSinceVersionAsync(Guid storeId, long sinceVersion, int limit = 500, CancellationToken cancellationToken = default);
    Task LogChangeAsync(SyncChangeLog changeLog, CancellationToken cancellationToken = default);
    Task LogChangesBatchAsync(IEnumerable<SyncChangeLog> changeLogs, CancellationToken cancellationToken = default);
}

public interface IUserRepository : IRepository<User, Guid>
{
    Task<User?> GetByUsernameWithRolesAsync(string username, CancellationToken cancellationToken = default);
    Task<User?> GetByEmailWithRolesAsync(string email, CancellationToken cancellationToken = default);
    Task<User?> GetWithRolesAndPermissionsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<RefreshToken?> GetRefreshTokenAsync(string token, CancellationToken cancellationToken = default);
    Task AddRefreshTokenAsync(RefreshToken refreshToken, CancellationToken cancellationToken = default);
    void UpdateRefreshToken(RefreshToken refreshToken);
}

public interface IEmployeeRepository : IRepository<Employee, Guid>
{
    Task<Employee?> GetByCodeAsync(Guid storeId, string employeeCode, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Employee>> GetByStoreAsync(Guid storeId, CancellationToken cancellationToken = default);
}

public interface ICustomerRepository : IRepository<Customer, Guid>
{
    Task<Customer?> GetByPhoneOrEmailAsync(string? phone, string? email, Guid? storeId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Customer>> SearchCustomersAsync(string query, Guid? storeId = null, int limit = 20, CancellationToken cancellationToken = default);
}
