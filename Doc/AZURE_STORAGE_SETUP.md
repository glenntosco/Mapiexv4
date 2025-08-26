# Azure Storage Setup for Product Image Sync

This guide explains how to configure Azure Blob Storage for syncing product images from SAP Business One to P4 Warehouse.

## Prerequisites

1. An Azure Storage Account
2. A container for product images
3. Storage Account access key

## Configuration Steps

### 1. Create Azure Storage Account

1. Log into Azure Portal (https://portal.azure.com)
2. Create a new Storage Account or use existing one
3. Note down:
   - Storage Account Name
   - Access Key (found in "Access keys" section)

### 2. Create Container

1. In your Storage Account, go to "Containers"
2. Create a new container named `product-images`
3. Set "Public access level" to "Blob" (anonymous read access for blobs only)

### 3. Update Configuration

Edit `appsettings.json` and update the AzureStorage section:

```json
{
  "AzureStorage": {
    "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=YOUR_ACCOUNT_NAME;AccountKey=YOUR_ACCESS_KEY;EndpointSuffix=core.windows.net",
    "ContainerName": "product-images",
    "BaseUrl": "https://YOUR_ACCOUNT_NAME.blob.core.windows.net",
    "UseAzureStorage": true,
    "UploadTimeoutSeconds": 300,
    "MaxFileSizeBytes": 10485760,
    "EnablePublicAccess": true
  }
}
```

Replace:
- `YOUR_ACCOUNT_NAME` with your actual Storage Account name
- `YOUR_ACCESS_KEY` with your actual Storage Account access key

### 4. Environment Variables (Optional)

For production, use environment variables instead of storing credentials in appsettings.json:

```bash
# Windows (PowerShell)
$env:AzureStorage__ConnectionString="DefaultEndpointsProtocol=https;AccountName=..."

# Linux/Mac
export AzureStorage__ConnectionString="DefaultEndpointsProtocol=https;AccountName=..."
```

## Features

When properly configured, the product image sync will:

1. **Fetch Images from SAP**: Retrieves product images from SAP B1 Attachments
2. **Upload to Azure**: Stores images in Azure Blob Storage
3. **Smart Sync**: Only uploads changed images (uses SHA256 hash for change detection)
4. **URL Generation**: Creates public URLs for P4W API
5. **Error Handling**: Image sync failures don't break product data sync

## Image Naming Convention

Images are stored with the following naming pattern:
```
{company}/{itemcode}_{hash}.{extension}
```

Example:
```
DEMO_MAPIEX_AVIATION/ITEM001_a3f5b2c1.jpg
```

## Troubleshooting

### "No valid combination of account information found" Error

This means the connection string is invalid. Check:
1. Account name is correct
2. Access key is correct
3. No typos in the connection string format

### Images Not Syncing

Check the logs for:
1. "Azure Blob Storage is not configured" - Configuration issue
2. "Image sync disabled" - UseAzureStorage is set to false
3. "Failed to upload image" - Network or permission issue

### Running Without Azure Storage

To run the integration without image sync:
1. Set `"UseAzureStorage": false` in appsettings.json
2. Or leave the ConnectionString empty
3. Products will sync without images

## Security Best Practices

1. **Never commit credentials** to source control
2. Use **Azure Key Vault** for production deployments
3. Rotate access keys regularly
4. Use **Managed Identity** when running in Azure
5. Enable **Storage Account firewall** and restrict access

## Testing

Test the configuration:
```bash
# Dry run with limit
dotnet run -- --operation ProductSync --company DEMO_MAPIEX_AVIATION --dryrun --limit 10

# Check logs for image sync messages
# Look for: "Uploading image for product {ItemCode}"
```

## Support

For issues with:
- **Azure Storage**: Check Azure Portal diagnostics
- **SAP Attachments**: Verify attachment entries in SAP
- **Integration**: Review logs in IntegrationLogs table