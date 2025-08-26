using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace P4WIntegration.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerSyncStatuses",
                columns: table => new
                {
                    CompanyName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CardCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LastSyncDateTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SyncHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerSyncStatuses", x => new { x.CompanyName, x.CardCode });
                });

            migrationBuilder.CreateTable(
                name: "ExecutionHistories",
                columns: table => new
                {
                    ExecutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Operation = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CompanyName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RecordsProcessed = table.Column<int>(type: "int", nullable: false),
                    ErrorCount = table.Column<int>(type: "int", nullable: false),
                    CommandLine = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionHistories", x => x.ExecutionId);
                });

            migrationBuilder.CreateTable(
                name: "GoodsDeliveryUploadStatuses",
                columns: table => new
                {
                    CompanyName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    P4WDeliveryId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SAPDocEntry = table.Column<int>(type: "int", nullable: true),
                    UploadDateTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoodsDeliveryUploadStatuses", x => new { x.CompanyName, x.P4WDeliveryId });
                });

            migrationBuilder.CreateTable(
                name: "GoodsReceiptUploadStatuses",
                columns: table => new
                {
                    CompanyName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    P4WReceiptId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SAPDocEntry = table.Column<int>(type: "int", nullable: true),
                    UploadDateTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoodsReceiptUploadStatuses", x => new { x.CompanyName, x.P4WReceiptId });
                });

            migrationBuilder.CreateTable(
                name: "IntegrationLogs",
                columns: table => new
                {
                    LogId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Level = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CompanyName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Exception = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExecutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationLogs", x => x.LogId);
                });

            migrationBuilder.CreateTable(
                name: "ProductSyncStatuses",
                columns: table => new
                {
                    CompanyName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ItemCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LastSyncDateTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SyncHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductSyncStatuses", x => new { x.CompanyName, x.ItemCode });
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrderSyncStatuses",
                columns: table => new
                {
                    CompanyName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DocEntry = table.Column<int>(type: "int", nullable: false),
                    DocNum = table.Column<int>(type: "int", nullable: false),
                    LastSyncDateTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SyncHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderSyncStatuses", x => new { x.CompanyName, x.DocEntry });
                });

            migrationBuilder.CreateTable(
                name: "ReturnUploadStatuses",
                columns: table => new
                {
                    CompanyName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    P4WReturnId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SAPDocEntry = table.Column<int>(type: "int", nullable: true),
                    UploadDateTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnUploadStatuses", x => new { x.CompanyName, x.P4WReturnId });
                });

            migrationBuilder.CreateTable(
                name: "SalesOrderSyncStatuses",
                columns: table => new
                {
                    CompanyName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DocEntry = table.Column<int>(type: "int", nullable: false),
                    DocNum = table.Column<int>(type: "int", nullable: false),
                    LastSyncDateTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SyncHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderSyncStatuses", x => new { x.CompanyName, x.DocEntry });
                });

            migrationBuilder.CreateTable(
                name: "SyncStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LastSyncDateTime = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VendorSyncStatuses",
                columns: table => new
                {
                    CompanyName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CardCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LastSyncDateTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SyncHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorSyncStatuses", x => new { x.CompanyName, x.CardCode });
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionHistories_CompanyName_StartTime",
                table: "ExecutionHistories",
                columns: new[] { "CompanyName", "StartTime" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationLogs_CompanyName_Timestamp",
                table: "IntegrationLogs",
                columns: new[] { "CompanyName", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncStates_CompanyName_EntityType",
                table: "SyncStates",
                columns: new[] { "CompanyName", "EntityType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerSyncStatuses");

            migrationBuilder.DropTable(
                name: "ExecutionHistories");

            migrationBuilder.DropTable(
                name: "GoodsDeliveryUploadStatuses");

            migrationBuilder.DropTable(
                name: "GoodsReceiptUploadStatuses");

            migrationBuilder.DropTable(
                name: "IntegrationLogs");

            migrationBuilder.DropTable(
                name: "ProductSyncStatuses");

            migrationBuilder.DropTable(
                name: "PurchaseOrderSyncStatuses");

            migrationBuilder.DropTable(
                name: "ReturnUploadStatuses");

            migrationBuilder.DropTable(
                name: "SalesOrderSyncStatuses");

            migrationBuilder.DropTable(
                name: "SyncStates");

            migrationBuilder.DropTable(
                name: "VendorSyncStatuses");
        }
    }
}
