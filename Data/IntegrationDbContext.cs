using Microsoft.EntityFrameworkCore;
using P4WIntegration.Models;

namespace P4WIntegration.Data;

public class IntegrationDbContext : DbContext
{
    public IntegrationDbContext(DbContextOptions<IntegrationDbContext> options)
        : base(options) { }

    // Sync status tables
    public DbSet<ProductSyncStatus> ProductSyncStatuses { get; set; }
    public DbSet<CustomerSyncStatus> CustomerSyncStatuses { get; set; }
    public DbSet<VendorSyncStatus> VendorSyncStatuses { get; set; }
    public DbSet<PurchaseOrderSyncStatus> PurchaseOrderSyncStatuses { get; set; }
    public DbSet<SalesOrderSyncStatus> SalesOrderSyncStatuses { get; set; }
    
    // Upload status tables
    public DbSet<GoodsReceiptUploadStatus> GoodsReceiptUploadStatuses { get; set; }
    public DbSet<GoodsDeliveryUploadStatus> GoodsDeliveryUploadStatuses { get; set; }
    public DbSet<ReturnUploadStatus> ReturnUploadStatuses { get; set; }
    
    // System tables
    public DbSet<SyncState> SyncStates { get; set; }
    public DbSet<IntegrationLog> IntegrationLogs { get; set; }
    public DbSet<ExecutionHistory> ExecutionHistories { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ProductSyncStatus configuration
        modelBuilder.Entity<ProductSyncStatus>()
            .HasKey(p => new { p.CompanyName, p.ItemCode });
        
        modelBuilder.Entity<ProductSyncStatus>()
            .Property(p => p.CompanyName).HasMaxLength(50);
        
        modelBuilder.Entity<ProductSyncStatus>()
            .Property(p => p.ItemCode).HasMaxLength(50);
        
        modelBuilder.Entity<ProductSyncStatus>()
            .Property(p => p.Status).HasMaxLength(20);
        
        modelBuilder.Entity<ProductSyncStatus>()
            .Property(p => p.SyncHash).HasMaxLength(64);

        // CustomerSyncStatus configuration
        modelBuilder.Entity<CustomerSyncStatus>()
            .HasKey(c => new { c.CompanyName, c.CardCode });
        
        modelBuilder.Entity<CustomerSyncStatus>()
            .Property(c => c.CompanyName).HasMaxLength(50);
        
        modelBuilder.Entity<CustomerSyncStatus>()
            .Property(c => c.CardCode).HasMaxLength(50);
        
        modelBuilder.Entity<CustomerSyncStatus>()
            .Property(c => c.Status).HasMaxLength(20);
        
        modelBuilder.Entity<CustomerSyncStatus>()
            .Property(c => c.SyncHash).HasMaxLength(64);

        // VendorSyncStatus configuration
        modelBuilder.Entity<VendorSyncStatus>()
            .HasKey(v => new { v.CompanyName, v.CardCode });
        
        modelBuilder.Entity<VendorSyncStatus>()
            .Property(v => v.CompanyName).HasMaxLength(50);
        
        modelBuilder.Entity<VendorSyncStatus>()
            .Property(v => v.CardCode).HasMaxLength(50);
        
        modelBuilder.Entity<VendorSyncStatus>()
            .Property(v => v.Status).HasMaxLength(20);
        
        modelBuilder.Entity<VendorSyncStatus>()
            .Property(v => v.SyncHash).HasMaxLength(64);

        // PurchaseOrderSyncStatus configuration
        modelBuilder.Entity<PurchaseOrderSyncStatus>()
            .HasKey(p => new { p.CompanyName, p.DocEntry });
        
        modelBuilder.Entity<PurchaseOrderSyncStatus>()
            .Property(p => p.CompanyName).HasMaxLength(50);
        
        modelBuilder.Entity<PurchaseOrderSyncStatus>()
            .Property(p => p.Status).HasMaxLength(20);
        
        modelBuilder.Entity<PurchaseOrderSyncStatus>()
            .Property(p => p.SyncHash).HasMaxLength(64);

        // SalesOrderSyncStatus configuration
        modelBuilder.Entity<SalesOrderSyncStatus>()
            .HasKey(s => new { s.CompanyName, s.DocEntry });
        
        modelBuilder.Entity<SalesOrderSyncStatus>()
            .Property(s => s.CompanyName).HasMaxLength(50);
        
        modelBuilder.Entity<SalesOrderSyncStatus>()
            .Property(s => s.Status).HasMaxLength(20);
        
        modelBuilder.Entity<SalesOrderSyncStatus>()
            .Property(s => s.SyncHash).HasMaxLength(64);

        // GoodsReceiptUploadStatus configuration
        modelBuilder.Entity<GoodsReceiptUploadStatus>()
            .HasKey(g => new { g.CompanyName, g.P4WReceiptId });
        
        modelBuilder.Entity<GoodsReceiptUploadStatus>()
            .Property(g => g.CompanyName).HasMaxLength(50);
        
        modelBuilder.Entity<GoodsReceiptUploadStatus>()
            .Property(g => g.P4WReceiptId).HasMaxLength(50);
        
        modelBuilder.Entity<GoodsReceiptUploadStatus>()
            .Property(g => g.Status).HasMaxLength(20);

        // GoodsDeliveryUploadStatus configuration
        modelBuilder.Entity<GoodsDeliveryUploadStatus>()
            .HasKey(g => new { g.CompanyName, g.P4WDeliveryId });
        
        modelBuilder.Entity<GoodsDeliveryUploadStatus>()
            .Property(g => g.CompanyName).HasMaxLength(50);
        
        modelBuilder.Entity<GoodsDeliveryUploadStatus>()
            .Property(g => g.P4WDeliveryId).HasMaxLength(50);
        
        modelBuilder.Entity<GoodsDeliveryUploadStatus>()
            .Property(g => g.Status).HasMaxLength(20);

        // ReturnUploadStatus configuration
        modelBuilder.Entity<ReturnUploadStatus>()
            .HasKey(r => new { r.CompanyName, r.P4WReturnId });
        
        modelBuilder.Entity<ReturnUploadStatus>()
            .Property(r => r.CompanyName).HasMaxLength(50);
        
        modelBuilder.Entity<ReturnUploadStatus>()
            .Property(r => r.P4WReturnId).HasMaxLength(50);
        
        modelBuilder.Entity<ReturnUploadStatus>()
            .Property(r => r.Status).HasMaxLength(20);

        // SyncState configuration
        modelBuilder.Entity<SyncState>()
            .HasKey(s => s.Id);
        
        modelBuilder.Entity<SyncState>()
            .HasIndex(s => new { s.CompanyName, s.EntityType })
            .IsUnique();
        
        modelBuilder.Entity<SyncState>()
            .Property(s => s.CompanyName).HasMaxLength(50);
        
        modelBuilder.Entity<SyncState>()
            .Property(s => s.EntityType).HasMaxLength(50);

        // IntegrationLog configuration
        modelBuilder.Entity<IntegrationLog>()
            .HasKey(i => i.LogId);
        
        modelBuilder.Entity<IntegrationLog>()
            .Property(i => i.LogId)
            .ValueGeneratedOnAdd();
        
        modelBuilder.Entity<IntegrationLog>()
            .HasIndex(i => new { i.CompanyName, i.Timestamp });
        
        modelBuilder.Entity<IntegrationLog>()
            .Property(i => i.Level).HasMaxLength(20);
        
        modelBuilder.Entity<IntegrationLog>()
            .Property(i => i.CompanyName).HasMaxLength(50);
        
        modelBuilder.Entity<IntegrationLog>()
            .Property(i => i.Operation).HasMaxLength(50);

        // ExecutionHistory configuration
        modelBuilder.Entity<ExecutionHistory>()
            .HasKey(e => e.ExecutionId);
        
        modelBuilder.Entity<ExecutionHistory>()
            .HasIndex(e => new { e.CompanyName, e.StartTime });
        
        modelBuilder.Entity<ExecutionHistory>()
            .Property(e => e.CompanyName).HasMaxLength(50);
        
        modelBuilder.Entity<ExecutionHistory>()
            .Property(e => e.Operation).HasMaxLength(50);
        
        modelBuilder.Entity<ExecutionHistory>()
            .Property(e => e.Status).HasMaxLength(20);
    }
}