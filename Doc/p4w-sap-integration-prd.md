# Product Requirements Document (PRD)
## P4 Warehouse - SAP Business One Service Layer Integration v2.0

---

## 1. Executive Summary

### 1.1 Purpose
This document defines requirements for migrating the existing P4 Warehouse (P4W) and SAP Business One (SAP B1) integration from the deprecated DI API to the modern Service Layer v2 API, implemented as a .NET 9 Console Application.

### 1.2 Project Goals
- **Replace deprecated DI API** with SAP B1 Service Layer v2
- **Maintain existing functionality** with zero business disruption  
- **Eliminate database locks** and stored procedure dependencies
- **Implement robust change tracking** via Azure SQL Database
- **Improve system reliability** through modern error handling
- **Enable better monitoring** and operational visibility

### 1.3 Key Success Criteria
- 100% feature parity with existing DI API integration
- Zero data loss during migration and operation
- < 5-minute synchronization latency
- 99.9% service availability
- Automated error recovery for transient failures

---

## 2. Current State Analysis

### 2.1 Existing System Problems
- **DI API Deprecation**: SAP no longer supports or updates the DI API
- **Database Locking**: Direct SQL operations cause table locks and performance issues
- **No Change Tracking**: Difficult to identify what data needs synchronization
- **Limited Visibility**: No centralized logging or monitoring
- **Manual Recovery**: Errors require manual intervention and restart
- **Stored Procedure Dependencies**: Tight coupling to SAP database internals

### 2.2 Migration Approach
- Direct SQL reads via DI API → Service Layer /SQLQuery endpoint
- DI API object updates → Service Layer OData operations
- Local tracking files → Azure SQL change tracking database
- Remove all lock/unlock operations
- Eliminate stored procedure calls
- Console app with scheduled execution

---

## 3. Functional Requirements

### 3.1 Data Synchronization Scope

#### 3.1.1 Download Flows (SAP B1 → P4 Warehouse)

**Products/Items**
- Source: OITM table (inventory items where InvntItem = 'Y')
- Data synchronized:
  - Item code, name, description
  - Inventory levels (OnHand, IsCommited, OnOrder)
  - Unit of measure and pack size calculation
  - Product images (if available)
- Frequency: Every 5 minutes
- Volume: ~10,000-50,000 items per company

**Customers**
- Source: OCRD table (CardType = 'C')
- Data synchronized:
  - Customer code and name
  - Tax/License number
  - Contact details (phone, email)
  - Billing/shipping addresses
- Frequency: Every 15 minutes
- Volume: ~5,000-20,000 customers per company

**Vendors/Suppliers**
- Source: OCRD table (CardType = 'S')
- Data synchronized:
  - Vendor code and name
  - Tax/License number
  - Contact information
- Frequency: Every 15 minutes
- Volume: ~500-2,000 vendors per company

**Purchase Orders**
- Source: OPOR table (DocStatus = 'O' - Open orders only)
- Data synchronized:
  - Order header (DocEntry, DocNum, dates)
  - Order lines (items, quantities, prices)
  - Vendor reference
- Frequency: Every 10 minutes
- Volume: ~100-500 open POs at any time

**Sales Orders**
- Source: ORDR table (DocStatus = 'O' - Open orders only)
- Data synchronized:
  - Order header information
  - Order lines for pick ticket generation
  - Customer reference
- Frequency: Every 2 minutes (critical for warehouse operations)
- Volume: ~200-1,000 open orders at any time

#### 3.1.2 Upload Flows (P4 Warehouse → SAP B1)

**Goods Receipts**
- Target: GoodsReceiptPOs in SAP B1
- Triggers: Completed receipts in P4W
- Updates: Inventory levels, PO status
- Frequency: Near real-time (< 1 minute after P4W completion)

**Goods Deliveries**
- Target: DeliveryNotes in SAP B1
- Source: Completed pick tickets in P4W
- Updates: Sales order fulfillment, inventory
- Frequency: Near real-time

**Returns Processing**
- Target: Returns documents in SAP B1
- Handles: Customer returns from P4W
- Updates: Inventory and customer records
- Frequency: As they occur

### 3.2 Business Rules

#### 3.2.1 Pack Size Calculation
```
Priority Order:
1. Check item-specific UoM conversion in SAP
2. Check barcode-based pack size
3. Default to pack size = 1
```

#### 3.2.2 Validation Rules
- Purchase Orders: Vendor and all items must exist in P4W before sync
- Sales Orders: Customer must exist, invalid items are logged but don't block order
- Goods Receipts: Must reference valid PO in SAP B1
- Deliveries: Must reference valid SO in SAP B1

#### 3.2.3 Error Handling Business Logic
- Invalid records are skipped and logged, not retried
- Partial success is allowed (e.g., 95 of 100 records succeed)
- Critical errors trigger alerts but don't stop other operations
- Duplicate prevention via tracking tables

---

## 4. Technical Architecture

### 4.1 High-Level Architecture

```
┌─────────────────────────────────────────────────────────────┐
│              .NET 9 Console Application                      │
│                                                              │
│  Executed via:                                              │
│  - Windows Task Scheduler (Production)                      │
│  - Cron Jobs (Linux)                                       │
│  - Manual Execution (Testing/Recovery)                      │
├─────────────────────────────────────────────────────────────┤
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐      │
│  │  Download    │  │   Upload     │  │  Monitoring  │      │
│  │  Operations  │  │  Operations  │  │   & Health   │      │
│  └──────────────┘  └──────────────┘  └──────────────┘      │
├─────────────────────────────────────────────────────────────┤
│             Service Layer Client | P4W API Client            │
└─────────────────────────────────────────────────────────────┘
           ↓                    ↓                    ↓
    ┌──────────┐         ┌──────────┐         ┌──────────┐
    │  SAP B1  │         │   P4W    │         │Azure SQL │
    │  Service │         │   API    │         │ Tracking │
    │  Layer   │         │          │         │    DB    │
    └──────────┘         └──────────┘         └──────────┘
```

### 4.2 Technology Stack

| Component | Technology | Purpose |
|-----------|------------|---------|
| Runtime | .NET 9.0 | Core application framework |
| Application Type | Console Application | Command-line executable |
| Database | Azure SQL Database | Change tracking and logging |
| ORM | Entity Framework Core 9 | Database operations |
| HTTP Client | HttpClient with Polly | Resilient API calls |
| Logging | Serilog | Structured logging |
| Configuration | appsettings.json + environment variables | Flexible configuration |
| Scheduling | Task Scheduler / Cron | Automated execution |

### 4.3 Console Application Structure

#### 4.3.1 Command Line Arguments
```bash
# Execute specific operation
P4WIntegration.exe --operation ProductSync --company COMPANY01

# Execute all operations for a company
P4WIntegration.exe --operation All --company COMPANY01

# Execute with custom config
P4WIntegration.exe --operation CustomerSync --config custom.json

# Dry run mode (no writes)
P4WIntegration.exe --operation ProductSync --company COMPANY01 --dryrun

# Available operations:
# - ProductSync
# - CustomerSync  
# - VendorSync
# - PurchaseOrderSync
# - SalesOrderSync
# - GoodsReceiptUpload
# - GoodsDeliveryUpload
# - ReturnUpload
# - All
```

#### 4.3.2 Program Structure
```csharp
// Program.cs
class Program
{
    static async Task<int> Main(string[] args)
    {
        // 1. Parse command line arguments
        // 2. Load configuration
        // 3. Initialize logging
        // 4. Initialize database
        // 5. Execute requested operation(s)
        // 6. Return exit code (0 = success, 1 = error)
    }
}
```

### 4.4 Database Design (Azure SQL with Entity Framework)

#### 4.4.1 Database Structure
Each company gets its own database: `P4I_<CompanyName>`, created and managed entirely through Entity Framework Code First migrations.

#### 4.4.2 Entity Framework Models

**Sync Status Entities**
```csharp
// Base sync status entity
public abstract class BaseSyncStatus
{
    public string CompanyName { get; set; }
    public DateTime LastSyncDateTime { get; set; }
    public string SyncHash { get; set; }
    public string Status { get; set; }
}

// Product sync status
public class ProductSyncStatus : BaseSyncStatus
{
    public string ItemCode { get; set; }
}

// Customer sync status
public class CustomerSyncStatus : BaseSyncStatus
{
    public string CardCode { get; set; }
}

// Vendor sync status
public class VendorSyncStatus : BaseSyncStatus
{
    public string CardCode { get; set; }
}

// Purchase order sync status
public class PurchaseOrderSyncStatus : BaseSyncStatus
{
    public int DocEntry { get; set; }
    public int DocNum { get; set; }
}

// Sales order sync status
public class SalesOrderSyncStatus : BaseSyncStatus
{
    public int DocEntry { get; set; }
    public int DocNum { get; set; }
}
```

**Upload Status Entities**
```csharp
// Base upload status entity
public abstract class BaseUploadStatus
{
    public string CompanyName { get; set; }
    public DateTime UploadDateTime { get; set; }
    public string Status { get; set; }
    public string ErrorMessage { get; set; }
}

// Goods receipt upload status
public class GoodsReceiptUploadStatus : BaseUploadStatus
{
    public string P4WReceiptId { get; set; }
    public int? SAPDocEntry { get; set; }
}

// Goods delivery upload status
public class GoodsDeliveryUploadStatus : BaseUploadStatus
{
    public string P4WDeliveryId { get; set; }
    public int? SAPDocEntry { get; set; }
}

// Return upload status
public class ReturnUploadStatus : BaseUploadStatus
{
    public string P4WReturnId { get; set; }
    public int? SAPDocEntry { get; set; }
}
```

**System Entities**
```csharp
// Sync state tracking
public class SyncState
{
    public int Id { get; set; }
    public string CompanyName { get; set; }
    public string EntityType { get; set; }
    public DateTime LastSyncDateTime { get; set; }
}

// Integration logging
public class IntegrationLog
{
    public int LogId { get; set; }
    public DateTime Timestamp { get; set; }
    public string Level { get; set; }
    public string CompanyName { get; set; }
    public string Operation { get; set; }
    public string Message { get; set; }
    public string Exception { get; set; }
    public Guid CorrelationId { get; set; }
    public Guid ExecutionId { get; set; }
}

// Execution history
public class ExecutionHistory
{
    public Guid ExecutionId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string Operation { get; set; }
    public string CompanyName { get; set; }
    public string Status { get; set; }
    public int RecordsProcessed { get; set; }
    public int ErrorCount { get; set; }
    public string CommandLine { get; set; }
}
```

#### 4.4.3 DbContext Configuration
```csharp
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

        // VendorSyncStatus configuration
        modelBuilder.Entity<VendorSyncStatus>()
            .HasKey(v => new { v.CompanyName, v.CardCode });
        
        modelBuilder.Entity<VendorSyncStatus>()
            .Property(v => v.CompanyName).HasMaxLength(50);
        
        modelBuilder.Entity<VendorSyncStatus>()
            .Property(v => v.CardCode).HasMaxLength(50);

        // PurchaseOrderSyncStatus configuration
        modelBuilder.Entity<PurchaseOrderSyncStatus>()
            .HasKey(p => new { p.CompanyName, p.DocEntry });

        // SalesOrderSyncStatus configuration
        modelBuilder.Entity<SalesOrderSyncStatus>()
            .HasKey(s => new { s.CompanyName, s.DocEntry });

        // GoodsReceiptUploadStatus configuration
        modelBuilder.Entity<GoodsReceiptUploadStatus>()
            .HasKey(g => new { g.CompanyName, g.P4WReceiptId });
        
        modelBuilder.Entity<GoodsReceiptUploadStatus>()
            .Property(g => g.P4WReceiptId).HasMaxLength(50);

        // GoodsDeliveryUploadStatus configuration
        modelBuilder.Entity<GoodsDeliveryUploadStatus>()
            .HasKey(g => new { g.CompanyName, g.P4WDeliveryId });
        
        modelBuilder.Entity<GoodsDeliveryUploadStatus>()
            .Property(g => g.P4WDeliveryId).HasMaxLength(50);

        // ReturnUploadStatus configuration
        modelBuilder.Entity<ReturnUploadStatus>()
            .HasKey(r => new { r.CompanyName, r.P4WReturnId });
        
        modelBuilder.Entity<ReturnUploadStatus>()
            .Property(r => r.P4WReturnId).HasMaxLength(50);

        // SyncState configuration
        modelBuilder.Entity<SyncState>()
            .HasKey(s => s.Id);
        
        modelBuilder.Entity<SyncState>()
            .HasIndex(s => new { s.CompanyName, s.EntityType })
            .IsUnique();
        
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
            .Property(i => i.Operation).HasMaxLength(50);

        // ExecutionHistory configuration
        modelBuilder.Entity<ExecutionHistory>()
            .HasKey(e => e.ExecutionId);
        
        modelBuilder.Entity<ExecutionHistory>()
            .HasIndex(e => new { e.CompanyName, e.StartTime });
        
        modelBuilder.Entity<ExecutionHistory>()
            .Property(e => e.Operation).HasMaxLength(50);
        
        modelBuilder.Entity<ExecutionHistory>()
            .Property(e => e.Status).HasMaxLength(20);
    }
}
```

#### 4.4.4 Migration Management
```csharp
// Initial migration creation
// Package Manager Console:
// Add-Migration InitialCreate
// Update-Database

// Programmatic migration on startup
public class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IntegrationDbContext>();
        
        // Apply any pending migrations
        await context.Database.MigrateAsync();
        
        // Create indexes if not exist (via migrations)
        // Seed initial data if needed
    }
}
```

### 4.5 SAP B1 Service Layer Integration

#### 4.5.1 Authentication Flow
```csharp
// 1. Login
POST /b1s/v2/Login
{
    "CompanyDB": "COMPANY_LIVE",
    "UserName": "integration_user",
    "Password": "password"
}

// 2. Store cookies: B1SESSION, ROUTEID
// 3. Include cookies in all subsequent requests
// 4. Handle 401 responses with automatic re-login
```

#### 4.5.2 Data Read Pattern (SQL Query)
```csharp
POST /b1s/v2/SQLQuery
Content-Type: application/json
Cookie: B1SESSION=xxx; ROUTEID=xxx

{
    "SqlCode": "SELECT ItemCode, ItemName, OnHand FROM OITM WHERE InvntItem = 'Y'"
}

// Returns: JSON array of matching records
```

#### 4.5.3 Data Write Pattern (OData)
```csharp
// Create new record
POST /b1s/v2/Items
Content-Type: application/json
Cookie: B1SESSION=xxx; ROUTEID=xxx

{
    "ItemCode": "ITEM001",
    "ItemName": "Product Name",
    ...
}

// Update existing record  
PATCH /b1s/v2/Items('ITEM001')
Content-Type: application/json
Cookie: B1SESSION=xxx; ROUTEID=xxx

{
    "ItemName": "Updated Name"
}
```

### 4.6 P4 Warehouse API Integration

#### 4.6.1 Authentication
- API Key in request headers
- Key rotation supported via configuration

#### 4.6.2 Standard Endpoints
- GET /api/products - Retrieve products
- POST /api/products - Create/update products
- GET /api/customers - Retrieve customers
- POST /api/purchase-orders - Create POs
- POST /api/pick-tickets - Create pick tickets
- GET /api/goods-receipts - Get completed receipts
- GET /api/deliveries - Get completed deliveries

---

## 5. Operation Implementation Details

### 5.1 Console Application Flow
Each operation follows the same pattern:
1. Parse command line arguments
2. Load configuration for specified company
3. Initialize logging with execution ID
4. Connect to Azure SQL tracking database
5. Execute specified operation
6. Log execution results
7. Return appropriate exit code

### 5.2 Specific Operation Implementations

#### 5.2.1 ProductSync Operation
**Command:**
```bash
P4WIntegration.exe --operation ProductSync --company COMPANY01
```

**SQL Query Used:**
```sql
SELECT 
    ItemCode, ItemName, ItemType, InvntItem,
    OnHand, IsCommited, OnOrder, 
    SalUnitMsr, PurUnitMsr,
    UpdateDate, CreateDate
FROM OITM 
WHERE InvntItem = 'Y'
    AND (UpdateDate >= @LastSyncDate OR CreateDate >= @LastSyncDate)
```

**Processing Logic:**
1. Execute /SQLQuery to get product list
2. For each product:
   - Calculate pack size from UoM tables
   - Check if changed via hash comparison
   - Map to P4W product structure
   - Upload product image if exists
   - Call P4W API to create/update
   - Update ProductSyncStatus

#### 5.2.2 CustomerSync Operation
**Command:**
```bash
P4WIntegration.exe --operation CustomerSync --company COMPANY01
```

**SQL Query Used:**
```sql
SELECT 
    CardCode, CardName, CardType,
    LicTradNum, Phone1, E_Mail,
    Address, City, State, ZipCode,
    UpdateDate, CreateDate
FROM OCRD 
WHERE CardType = 'C'
    AND (UpdateDate >= @LastSyncDate OR CreateDate >= @LastSyncDate)
```

**Processing Logic:**
1. Query customers via /SQLQuery
2. Transform to P4W customer format
3. Batch upload to P4W (configurable batch size)
4. Track in CustomerSyncStatus

#### 5.2.3 PurchaseOrderSync Operation
**Command:**
```bash
P4WIntegration.exe --operation PurchaseOrderSync --company COMPANY01
```

**SQL Query Used:**
```sql
-- Header query
SELECT 
    DocEntry, DocNum, DocDate, DocDueDate,
    CardCode, CardName, DocTotal, DocStatus
FROM OPOR 
WHERE DocStatus = 'O'

-- Lines query  
SELECT 
    DocEntry, LineNum, ItemCode, 
    Quantity, Price, LineTotal
FROM POR1
WHERE DocEntry = @DocEntry
```

**Processing Logic:**
1. Get open PO headers
2. For each PO:
   - Verify vendor exists in P4W
   - Get PO lines
   - Verify all products exist in P4W
   - Create PO in P4W
   - Update PurchaseOrderSyncStatus

#### 5.2.4 SalesOrderSync Operation
**Command:**
```bash
P4WIntegration.exe --operation SalesOrderSync --company COMPANY01
```

**Processing Logic:**
1. Get open SO headers from SAP B1
2. For each SO:
   - Verify customer exists in P4W
   - Get SO lines
   - Create pick ticket in P4W
   - Update SalesOrderSyncStatus

#### 5.2.5 GoodsReceiptUpload Operation
**Command:**
```bash
P4WIntegration.exe --operation GoodsReceiptUpload --company COMPANY01
```

**Processing Logic:**
1. Query P4W for completed receipts
2. For each receipt:
   - Check if already uploaded (via tracking table)
   - Map to SAP Goods Receipt PO structure
   - POST to /b1s/v2/GoodsReceiptPOs
   - Update GoodsReceiptUploadStatus
   - Mark as processed in P4W

### 5.3 Scheduling Configuration

#### 5.3.1 Windows Task Scheduler Setup
```xml
<!-- ProductSync Task - Every 5 minutes -->
<Task>
    <Triggers>
        <TimeTrigger>
            <Repetition>
                <Interval>PT5M</Interval>
            </Repetition>
        </TimeTrigger>
    </Triggers>
    <Actions>
        <Exec>
            <Command>C:\P4WIntegration\P4WIntegration.exe</Command>
            <Arguments>--operation ProductSync --company COMPANY01</Arguments>
        </Exec>
    </Actions>
</Task>
```

#### 5.3.2 Linux Cron Setup
```bash
# Crontab entries
*/5 * * * * /opt/p4w/P4WIntegration --operation ProductSync --company COMPANY01
*/15 * * * * /opt/p4w/P4WIntegration --operation CustomerSync --company COMPANY01
*/15 * * * * /opt/p4w/P4WIntegration --operation VendorSync --company COMPANY01
*/10 * * * * /opt/p4w/P4WIntegration --operation PurchaseOrderSync --company COMPANY01
*/2 * * * * /opt/p4w/P4WIntegration --operation SalesOrderSync --company COMPANY01
* * * * * /opt/p4w/P4WIntegration --operation GoodsReceiptUpload --company COMPANY01
* * * * * /opt/p4w/P4WIntegration --operation GoodsDeliveryUpload --company COMPANY01
```

#### 5.3.3 Schedule Summary
| Operation | Frequency | Priority |
|-----------|-----------|----------|
| ProductSync | Every 5 minutes | Normal |
| CustomerSync | Every 15 minutes | Normal |
| VendorSync | Every 15 minutes | Normal |
| PurchaseOrderSync | Every 10 minutes | Normal |
| SalesOrderSync | Every 2 minutes | High |
| GoodsReceiptUpload | Every minute | Critical |
| GoodsDeliveryUpload | Every minute | Critical |

---

## 6. Non-Functional Requirements

### 6.1 Performance Requirements

| Metric | Target | Measurement Method |
|--------|--------|-------------------|
| Execution Time | < 30 seconds per operation | Time from start to exit |
| Sync Latency | < 5 minutes | Time from source change to target update |
| Throughput | 1000 records/minute | Records processed per minute |
| Service Layer Response | < 2 seconds | 95th percentile API response time |
| Database Query Time | < 500ms | Average query execution time |
| Memory Usage | < 500MB | Peak memory consumption |
| Startup Time | < 5 seconds | Time to first operation |

### 6.2 Reliability & Availability

| Requirement | Target | Implementation |
|-------------|--------|----------------|
| Execution Success Rate | 99.9% | Error handling, retry logic |
| Data Integrity | 100% | Transaction consistency, validation |
| Error Recovery | Automatic | Retry with exponential backoff |
| Concurrent Executions | Prevented | File-based locking mechanism |
| Failure Isolation | Complete | One operation failure doesn't affect others |

### 6.3 Security Requirements

- **Authentication**: Service Layer credentials per company
- **API Security**: API keys for P4W, stored encrypted
- **Connection Security**: TLS 1.2+ for all connections
- **Credential Storage**: Environment variables or encrypted config
- **Logging**: No passwords or sensitive data in logs
- **Access Control**: Restricted execution permissions

### 6.4 Operational Requirements

- **Deployment**: Simple xcopy deployment
- **Configuration**: External configuration files
- **Monitoring**: Exit codes for success/failure
- **Alerting**: Task Scheduler alerts on failure
- **Logging**: File-based and database logging
- **Retention**: 90 days of logs, 7 years of sync history

---

## 7. Error Handling Strategy

### 7.1 Exit Codes

| Code | Meaning | Action Required |
|------|---------|-----------------|
| 0 | Success | None |
| 1 | General failure | Check logs |
| 2 | Configuration error | Fix configuration |
| 3 | Database connection failed | Check Azure SQL |
| 4 | SAP B1 authentication failed | Check credentials |
| 5 | P4W API error | Check P4W availability |
| 10 | Partial success | Review logs for failed records |

### 7.2 Error Classification

| Type | Examples | Handling | Retry |
|------|----------|----------|-------|
| Transient | Network timeout, API throttling | Automatic retry | Yes - exponential backoff |
| Authentication | Session expired, invalid credentials | Re-authenticate | Yes - once |
| Data Validation | Missing required field | Log and skip record | No |
| Business Logic | Duplicate key, constraint violation | Log to error queue | No |
| System | Out of memory, database down | Exit with error code | No - manual fix required |

### 7.3 Retry Policy Configuration
```csharp
// Polly retry policy example
var retryPolicy = Policy
    .Handle<HttpRequestException>()
    .OrResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
    .WaitAndRetryAsync(
        retryCount: 3,
        sleepDurationProvider: retryAttempt => 
            TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
        onRetry: (outcome, timespan, retryCount, context) =>
        {
            logger.LogWarning($"Retry {retryCount} after {timespan}s");
        });
```

### 7.4 Error Notification
- Task Scheduler email on failure (exit code != 0)
- Database logging for all errors
- Daily error summary report
- Critical error alerts via monitoring system

---

## 8. Monitoring & Observability

### 8.1 Logging

#### 8.1.1 Log Outputs
- **Console**: Real-time execution feedback
- **File**: Detailed logs in C:\Logs\P4WIntegration\
- **Database**: Structured logs in IntegrationLogs table
- **Windows Event Log**: Critical errors only

#### 8.1.2 Log Levels
```csharp
// Serilog configuration
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File("logs/p4w-.txt", rollingInterval: RollingInterval.Day)
    .WriteTo.MSSqlServer(connectionString, "IntegrationLogs")
    .CreateLogger();
```

### 8.2 Execution Tracking

```sql
-- Query to monitor execution history
SELECT 
    Operation,
    CompanyName,
    StartTime,
    EndTime,
    DATEDIFF(second, StartTime, EndTime) as DurationSeconds,
    Status,
    RecordsProcessed,
    ErrorCount
FROM ExecutionHistory
WHERE StartTime >= DATEADD(hour, -24, GETDATE())
ORDER BY StartTime DESC
```

### 8.3 Key Metrics

| Metric | Description | Alert Threshold |
|--------|-------------|-----------------|
| execution_success_rate | % successful executions | < 95% |
| execution_duration | Time per execution | > 60 seconds |
| records_processed | Records per execution | < 100 (if expected higher) |
| error_rate | Errors per execution | > 10 |
| last_execution_time | Time since last run | > 2x scheduled interval |

### 8.4 Monitoring Queries

```sql
-- Failed executions in last 24 hours
SELECT * FROM ExecutionHistory 
WHERE Status = 'Failed' 
  AND StartTime >= DATEADD(hour, -24, GETDATE())

-- Operations not run recently
SELECT 
    Operation,
    MAX(StartTime) as LastRun,
    DATEDIFF(minute, MAX(StartTime), GETDATE()) as MinutesSinceRun
FROM ExecutionHistory
GROUP BY Operation
HAVING DATEDIFF(minute, MAX(StartTime), GETDATE()) > 60

-- Error summary by type
SELECT 
    Operation,
    COUNT(*) as ErrorCount,
    LEFT(Exception, 100) as ErrorType
FROM IntegrationLogs
WHERE Level = 'Error'
  AND Timestamp >= DATEADD(hour, -24, GETDATE())
GROUP BY Operation, LEFT(Exception, 100)
```

---

## 9. Testing Strategy

### 9.1 Test Execution Modes

```bash
# Dry run mode - no writes to target systems
P4WIntegration.exe --operation ProductSync --company TEST01 --dryrun

# Test with limited records
P4WIntegration.exe --operation CustomerSync --company TEST01 --limit 10

# Verbose logging for debugging
P4WIntegration.exe --operation All --company TEST01 --verbose

# Specific date range testing
P4WIntegration.exe --operation ProductSync --company TEST01 --from "2024-01-01" --to "2024-01-31"
```

### 9.2 Test Scenarios

**Unit Tests:**
- Command line argument parsing
- Configuration loading
- Data transformation logic
- Pack size calculation
- Hash generation for change detection

**Integration Tests:**
- Service Layer authentication
- /SQLQuery execution
- OData operations
- P4W API calls
- Azure SQL operations

**End-to-End Tests:**
- Full sync from empty state
- Delta sync with changes
- Error handling and recovery
- Concurrent execution prevention
- Performance under load

### 9.3 Test Data Requirements
- Test company database in SAP B1
- Test instance of P4W API
- Test Azure SQL database
- Sample data:
  - 1,000 products with various UoMs
  - 500 customers
  - 100 vendors
  - 50 open purchase orders
  - 100 open sales orders

---

## 10. Migration Plan

### 10.1 Pre-Migration (Week 1)
- Set up Azure SQL databases
- Deploy console application to servers
- Configure Task Scheduler/Cron jobs (disabled)
- Test connectivity to all systems
- Create rollback scripts

### 10.2 Parallel Run (Weeks 2-3)
- Enable console app in read-only mode
- Run alongside DI API system
- Compare outputs
- Performance tuning
- Fix discrepancies

### 10.3 Pilot Migration (Week 4)
- Select one small company
- Disable DI API for pilot company
- Enable full console app functionality
- 48-hour monitoring period
- Document issues and fixes

### 10.4 Full Migration (Weeks 5-6)
- Migrate companies in batches (5 per day)
- Update Task Scheduler for each company
- Disable corresponding DI API tasks
- Validation after each company
- Keep DI API available for rollback

### 10.5 Post-Migration (Week 7+)
- Performance optimization
- Remove DI API scheduled tasks
- Archive DI API application
- Documentation updates
- Knowledge transfer sessions

---

## 11. Configuration Management

### 11.1 Application Settings (appsettings.json)
```json
{
  "ApplicationSettings": {
    "ApplicationName": "P4W-SAP-Integration",
    "Version": "2.0.0",
    "Environment": "Production",
    "DefaultBatchSize": 100,
    "DefaultRetryCount": 3,
    "MaxConcurrentOperations": 1
  },
  "AzureSQL": {
    "ConnectionString": "Server=tcp:xxx.database.windows.net;Database=P4I_{CompanyName};",
    "CommandTimeout": 30,
    "EnableRetryOnFailure": true
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning"
    },
    "FilePath": "C:\\Logs\\P4WIntegration",
    "RetainDays": 90
  }
}
```

### 11.2 Company Configuration (companies.json)
```json
{
  "Companies": [
    {
      "CompanyCode": "COMPANY01",
      "Active": true,
      "SapB1": {
        "ServiceLayerUrl": "https://sapserver:50000/b1s/v2/",
        "CompanyDb": "COMPANY01_LIVE",
        "UserName": "integration_user",
        "Password": "${SAP_PASSWORD}",
        "SessionTimeout": 30,
        "MaxConcurrentRequests": 5
      },
      "P4Warehouse": {
        "ApiUrl": "https://p4w-api.com/api/",
        "ApiKey": "${P4W_API_KEY}",
        "CompanyIdentifier": "COMP01",
        "MaxBatchSize": 100
      },
      "OperationSettings": {
        "ProductSync": {
          "Enabled": true,
          "BatchSize": 100,
          "DeltaSyncEnabled": true
        },
        "CustomerSync": {
          "Enabled": true,
          "BatchSize": 200,
          "DeltaSyncEnabled": true
        },
        "VendorSync": {
          "Enabled": true,
          "BatchSize": 50
        },
        "PurchaseOrderSync": {
          "Enabled": true,
          "ValidateReferences": true
        },
        "SalesOrderSync": {
          "Enabled": true,
          "CreatePickTickets": true
        },
        "GoodsReceiptUpload": {
          "Enabled": true,
          "UpdateInventory": true
        },
        "GoodsDeliveryUpload": {
          "Enabled": true,
          "UpdateSalesOrders": true
        }
      }
    }
  ]
}
```

### 11.3 Environment Variables
```bash
# Windows
setx SAP_PASSWORD "encrypted_password"
setx P4W_API_KEY "api_key_value"
setx AZURE_SQL_PASSWORD "sql_password"

# Linux
export SAP_PASSWORD="encrypted_password"
export P4W_API_KEY="api_key_value"
export AZURE_SQL_PASSWORD="sql_password"
```

---

## 12. Deployment & Operations

### 12.1 Deployment Package Structure
```
C:\P4WIntegration\
├── P4WIntegration.exe
├── P4WIntegration.dll
├── appsettings.json
├── companies.json
├── Microsoft.EntityFrameworkCore.dll
├── Serilog.dll
├── Polly.dll
├── [other dependencies]
└── Logs\
    └── [log files]
```

### 12.2 Installation Steps
1. Create installation directory
2. Copy application files
3. Configure appsettings.json
4. Configure companies.json
5. Set environment variables
6. Create Task Scheduler tasks
7. Test with dry run
8. Enable scheduled tasks

### 12.3 Operational Procedures

#### Daily Operations
- Review execution history for failures
- Check error logs for patterns
- Verify all scheduled tasks ran
- Monitor sync latency

#### Weekly Operations
- Performance analysis
- Database maintenance (index rebuild)
- Log file cleanup
- Backup configuration files

#### Monthly Operations
- Review and optimize schedules
- Update API credentials if needed
- Security patches
- Performance tuning

### 12.4 Troubleshooting Guide

| Issue | Symptoms | Resolution |
|-------|----------|------------|
| Task not running | No recent executions | Check Task Scheduler, verify schedule |
| Authentication errors | Exit code 4 | Update credentials, check account lock |
| Slow performance | Execution > 60 seconds | Check network, database indexes |
| High memory usage | > 1GB RAM | Review batch sizes, check for memory leaks |
| Duplicate data | Records processed twice | Check concurrent execution lock |

---

## 13. Support & Maintenance

### 13.1 Support Matrix

| Component | Owner | Contact |
|-----------|-------|---------|
| Console Application | Development Team | dev-team@company.com |
| Task Scheduler | IT Operations | it-ops@company.com |
| SAP B1 Service Layer | SAP Team | sap-admin@company.com |
| P4W API | P4W Team | p4w-support@company.com |
| Azure SQL | Database Team | dba@company.com |

### 13.2 Maintenance Windows
- **Scheduled**: Sunday 2:00-4:00 AM
- **Emergency**: As needed with notification
- **Updates**: Monthly patches
- **Major upgrades**: Quarterly

### 13.3 Documentation Deliverables
- Installation guide
- Configuration reference
- Operations runbook
- Troubleshooting guide
- API integration specifications
- Database schema documentation

---

## 14. Risks & Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| Concurrent execution conflicts | High | Medium | File-based locking mechanism |
| Task Scheduler failures | High | Low | Monitoring, redundant schedules |
| Memory leaks in long-running operations | Medium | Low | Regular application restart |
| Service Layer API changes | High | Low | Version pinning, regression tests |
| Network connectivity issues | Medium | Medium | Retry logic, error handling |
| Credential expiration | High | Medium | Monitoring, rotation procedures |

---

## 15. Success Metrics

### 15.1 Technical Metrics (Month 1)
- ✓ All scheduled tasks executing successfully
- ✓ < 5-minute sync latency achieved
- ✓ Zero data loss incidents
- ✓ 99.9% execution success rate

### 15.2 Business Metrics (Month 3)
- ✓ 50% reduction in sync-related tickets
- ✓ Complete migration from DI API
- ✓ All companies operational
- ✓ Operational costs reduced by 30%

### 15.3 Long-term Goals (Month 6)
- Automated deployment pipeline
- Performance optimization completed
- Disaster recovery tested
- Full documentation completed

---

## 16. Approval Matrix

| Stakeholder | Role | Approval Required For |
|-------------|------|----------------------|
| IT Director | Sponsor | Budget, timeline, resources |
| Operations Manager | Owner | Functional requirements, schedule |
| SAP B1 Administrator | Technical | SAP integration approach |
| P4W Team Lead | Technical | P4W API changes |
| Infrastructure Team | Technical | Server and scheduling setup |

---

## Appendices

### Appendix A: Command Line Reference

```bash
# Basic Operations
P4WIntegration.exe --operation <OperationType> --company <CompanyCode>

# Available Operations:
# ProductSync, CustomerSync, VendorSync, PurchaseOrderSync, 
# SalesOrderSync, GoodsReceiptUpload, GoodsDeliveryUpload, 
# ReturnUpload, All

# Optional Parameters:
--config <path>     # Custom config file
--dryrun           # No writes to target systems
--verbose          # Detailed logging
--limit <n>        # Process only N records
--from <date>      # Start date for sync
--to <date>        # End date for sync
--force           # Ignore last sync timestamp

# Examples:
P4WIntegration.exe --operation All --company COMP01
P4WIntegration.exe --operation ProductSync --company COMP01 --dryrun
P4WIntegration.exe --operation CustomerSync --company COMP01 --limit 100
```

### Appendix B: SQL Query Reference

#### B.1 Product Sync Query
```sql
SELECT 
    T0.ItemCode, T0.ItemName, T0.ItemType, 
    T0.InvntItem, T0.OnHand, T0.IsCommited, T0.OnOrder,
    T0.SalUnitMsr, T0.PurUnitMsr, T0.InvntryUom,
    T0.UpdateDate, T0.CreateDate,
    T1.UomCode, T1.UomEntry
FROM OITM T0
LEFT JOIN OUOM T1 ON T0.InvntryUom = T1.UomEntry
WHERE T0.InvntItem = 'Y'
    AND (T0.UpdateDate >= @LastSync OR T0.CreateDate >= @LastSync)
```

#### B.2 Customer Sync Query
```sql
SELECT 
    CardCode, CardName, CardType, GroupCode,
    LicTradNum, Phone1, Phone2, Cellular, E_Mail,
    Address, City, County, State, ZipCode, Country,
    CreditLine, Balance, OrdersBal,
    UpdateDate, CreateDate
FROM OCRD 
WHERE CardType = 'C'
    AND (UpdateDate >= @LastSync OR CreateDate >= @LastSync)
```

### Appendix C: Error Code Reference

| Code | Description | Resolution |
|------|-------------|------------|
| 0 | Success | None required |
| 1 | General failure | Check logs for details |
| 2 | Configuration error | Verify config files |
| 3 | Database connection failed | Check connection string |
| 4 | SAP authentication failed | Update credentials |
| 5 | P4W API error | Check API availability |
| 10 | Partial success | Review failed records |

### Appendix D: Performance Baselines

| Operation | Expected Duration | Records/Minute |
|-----------|------------------|----------------|
| ProductSync (10k items) | < 10 minutes | 1000 |
| CustomerSync (5k) | < 5 minutes | 1000 |
| VendorSync (1k) | < 2 minutes | 500 |
| PurchaseOrderSync | < 30 seconds | 100 |
| SalesOrderSync | < 30 seconds | 200 |
| GoodsReceiptUpload | < 10 seconds | 50 |
| GoodsDeliveryUpload | < 10 seconds | 50 |