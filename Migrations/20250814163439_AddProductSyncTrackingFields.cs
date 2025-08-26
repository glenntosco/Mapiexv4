using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace P4WIntegration.Migrations
{
    /// <inheritdoc />
    public partial class AddProductSyncTrackingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedDateTime",
                table: "VendorSyncStatuses",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "P4WSyncDateTime",
                table: "VendorSyncStatuses",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SyncedToP4W",
                table: "VendorSyncStatuses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedDateTime",
                table: "SalesOrderSyncStatuses",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "P4WSyncDateTime",
                table: "SalesOrderSyncStatuses",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SyncedToP4W",
                table: "SalesOrderSyncStatuses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedDateTime",
                table: "PurchaseOrderSyncStatuses",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "P4WSyncDateTime",
                table: "PurchaseOrderSyncStatuses",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SyncedToP4W",
                table: "PurchaseOrderSyncStatuses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedDateTime",
                table: "ProductSyncStatuses",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "P4WSyncDateTime",
                table: "ProductSyncStatuses",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SyncedToP4W",
                table: "ProductSyncStatuses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedDateTime",
                table: "CustomerSyncStatuses",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "P4WSyncDateTime",
                table: "CustomerSyncStatuses",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SyncedToP4W",
                table: "CustomerSyncStatuses",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedDateTime",
                table: "VendorSyncStatuses");

            migrationBuilder.DropColumn(
                name: "P4WSyncDateTime",
                table: "VendorSyncStatuses");

            migrationBuilder.DropColumn(
                name: "SyncedToP4W",
                table: "VendorSyncStatuses");

            migrationBuilder.DropColumn(
                name: "CreatedDateTime",
                table: "SalesOrderSyncStatuses");

            migrationBuilder.DropColumn(
                name: "P4WSyncDateTime",
                table: "SalesOrderSyncStatuses");

            migrationBuilder.DropColumn(
                name: "SyncedToP4W",
                table: "SalesOrderSyncStatuses");

            migrationBuilder.DropColumn(
                name: "CreatedDateTime",
                table: "PurchaseOrderSyncStatuses");

            migrationBuilder.DropColumn(
                name: "P4WSyncDateTime",
                table: "PurchaseOrderSyncStatuses");

            migrationBuilder.DropColumn(
                name: "SyncedToP4W",
                table: "PurchaseOrderSyncStatuses");

            migrationBuilder.DropColumn(
                name: "CreatedDateTime",
                table: "ProductSyncStatuses");

            migrationBuilder.DropColumn(
                name: "P4WSyncDateTime",
                table: "ProductSyncStatuses");

            migrationBuilder.DropColumn(
                name: "SyncedToP4W",
                table: "ProductSyncStatuses");

            migrationBuilder.DropColumn(
                name: "CreatedDateTime",
                table: "CustomerSyncStatuses");

            migrationBuilder.DropColumn(
                name: "P4WSyncDateTime",
                table: "CustomerSyncStatuses");

            migrationBuilder.DropColumn(
                name: "SyncedToP4W",
                table: "CustomerSyncStatuses");
        }
    }
}
