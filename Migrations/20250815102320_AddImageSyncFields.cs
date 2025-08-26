using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace P4WIntegration.Migrations
{
    /// <inheritdoc />
    public partial class AddImageSyncFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageHash",
                table: "VendorSyncStatuses",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ImageSyncDateTime",
                table: "VendorSyncStatuses",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "VendorSyncStatuses",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageHash",
                table: "SalesOrderSyncStatuses",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ImageSyncDateTime",
                table: "SalesOrderSyncStatuses",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "SalesOrderSyncStatuses",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageHash",
                table: "PurchaseOrderSyncStatuses",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ImageSyncDateTime",
                table: "PurchaseOrderSyncStatuses",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "PurchaseOrderSyncStatuses",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageHash",
                table: "ProductSyncStatuses",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ImageSyncDateTime",
                table: "ProductSyncStatuses",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "ProductSyncStatuses",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageHash",
                table: "CustomerSyncStatuses",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ImageSyncDateTime",
                table: "CustomerSyncStatuses",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "CustomerSyncStatuses",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageHash",
                table: "VendorSyncStatuses");

            migrationBuilder.DropColumn(
                name: "ImageSyncDateTime",
                table: "VendorSyncStatuses");

            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "VendorSyncStatuses");

            migrationBuilder.DropColumn(
                name: "ImageHash",
                table: "SalesOrderSyncStatuses");

            migrationBuilder.DropColumn(
                name: "ImageSyncDateTime",
                table: "SalesOrderSyncStatuses");

            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "SalesOrderSyncStatuses");

            migrationBuilder.DropColumn(
                name: "ImageHash",
                table: "PurchaseOrderSyncStatuses");

            migrationBuilder.DropColumn(
                name: "ImageSyncDateTime",
                table: "PurchaseOrderSyncStatuses");

            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "PurchaseOrderSyncStatuses");

            migrationBuilder.DropColumn(
                name: "ImageHash",
                table: "ProductSyncStatuses");

            migrationBuilder.DropColumn(
                name: "ImageSyncDateTime",
                table: "ProductSyncStatuses");

            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "ProductSyncStatuses");

            migrationBuilder.DropColumn(
                name: "ImageHash",
                table: "CustomerSyncStatuses");

            migrationBuilder.DropColumn(
                name: "ImageSyncDateTime",
                table: "CustomerSyncStatuses");

            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "CustomerSyncStatuses");
        }
    }
}
