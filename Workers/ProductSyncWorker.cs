using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using P4WIntegration.Configuration;
using P4WIntegration.Data;
using P4WIntegration.Models;
using P4WIntegration.Services;
using P4WIntegration.Utilities;
using System.Linq;
using System.Text.Json;

namespace P4WIntegration.Workers;

public class ProductSyncWorker : IWorker
{
    private readonly ILogger<ProductSyncWorker> _logger;
    private readonly ServiceLayerClient _serviceLayer;
    private readonly P4WarehouseClient _p4wClient;
    private readonly IntegrationDbContext _dbContext;
    private readonly CommandLineOptions _options;
    private readonly Company _company;
    private readonly Schedule? _schedule;
    private readonly AzureBlobService? _azureBlobService;
    private string? _clientId; // Cache the client ID

    public ProductSyncWorker(
        ILogger<ProductSyncWorker> logger,
        ServiceLayerClient serviceLayer,
        IntegrationDbContext dbContext,
        CommandLineOptions options,
        CompanyConfig config,
        ILoggerFactory loggerFactory,
        AzureBlobService? azureBlobService = null)
    {
        _logger = logger;
        _serviceLayer = serviceLayer;
        _dbContext = dbContext;
        _options = options;
        _company = config.Companies.First(c => c.CompanyName.Equals(options.Company, StringComparison.OrdinalIgnoreCase));
        _schedule = config.Schedules.FirstOrDefault(s => s.Name == "ProductSync");
        _azureBlobService = azureBlobService;
        var p4wLogger = loggerFactory.CreateLogger<P4WarehouseClient>();
        _p4wClient = new P4WarehouseClient(_company.P4WarehouseApiKey, _company.SapB1.ClientName, p4wLogger);
    }

    public async Task<WorkerResult> ExecuteAsync()
    {
        _logger.LogInformation("Starting ProductSync for company {Company}", _company.CompanyName);

        var result = new WorkerResult { Success = true };
        var processedCount = 0;
        var errorCount = 0;

        try
        {
            // Get client ID from P4W API if not already cached
            if (string.IsNullOrEmpty(_clientId))
            {
                _clientId = await _p4wClient.GetClientIdAsync();
                if (string.IsNullOrEmpty(_clientId))
                {
                    // Fallback to using the configured client name
                    _clientId = _company.SapB1.ClientName;
                    _logger.LogWarning("Could not retrieve client ID from P4W API, using configured ClientName: {ClientName}", _clientId);
                    
                    if (string.IsNullOrEmpty(_clientId))
                    {
                        _logger.LogError("No client ID available - neither from P4W API nor configuration");
                        result.Success = false;
                        result.ErrorCount = 1;
                        return result;
                    }
                }
            }
            
            // Fetch ItemGroups from Service Layer API to get GroupName mapping
            var itemGroupsMapping = await FetchItemGroupsMapping();
            _logger.LogInformation("Loaded {Count} item groups from Service Layer", itemGroupsMapping.Count);
            
            // Get last sync date
            var lastSync = await GetLastSyncDate();
            _logger.LogInformation("Last sync date: {LastSync}", lastSync);

            // Fetch all products using OData pagination
            var products = await FetchAllProductsWithPagination(lastSync);
            
            if (products == null || products.Count == 0)
            {
                _logger.LogInformation("No products to sync");
                return result;
            }

            _logger.LogInformation("Found {Count} products to process", products.Count);

            // Apply limit if specified
            if (_options.Limit.HasValue)
            {
                products = products.Take(_options.Limit.Value).ToList();
                _logger.LogInformation("Limited to {Limit} products", _options.Limit.Value);
            }

            // Process products in batches
            var batchSize = _company.Settings.ProductBatchSize;
            for (int i = 0; i < products.Count; i += batchSize)
            {
                var batch = products.Skip(i).Take(batchSize).ToList();
                
                foreach (var product in batch)
                {
                    try
                    {
                        await ProcessProduct(product, itemGroupsMapping);
                        processedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing product {ItemCode}", 
                            product.GetValueOrDefault("ItemCode", "Unknown"));
                        errorCount++;
                    }
                }

                // Save batch to database (save even in dry run to track what was processed)
                await _dbContext.SaveChangesAsync();
                
                _logger.LogInformation("Processed batch {BatchNum}/{TotalBatches}", 
                    (i / batchSize) + 1, 
                    (products.Count + batchSize - 1) / batchSize);
            }

            // Update sync state
            if (!_options.DryRun)
            {
                await UpdateSyncState();
            }

            result.RecordsProcessed = processedCount;
            result.ErrorCount = errorCount;
            result.Success = errorCount == 0;
            result.PartialSuccess = errorCount > 0 && processedCount > 0;

            // Report sync statistics
            var (total, synced, lastSyncTime) = await GetSyncStatusAsync();
            _logger.LogInformation("ProductSync completed - Processed: {Processed}, Errors: {Errors}", 
                processedCount, errorCount);
            _logger.LogInformation("Database sync status - Total tracked: {Total}, Synced to P4W: {Synced}, Last sync: {LastSync}", 
                total, synced, lastSyncTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in ProductSync");
            result.Success = false;
            result.ErrorCount = 1;
        }

        return result;
    }

    private async Task<DateTime> GetLastSyncDate()
    {
        // Check if Full sync mode is configured
        if (_schedule?.SyncMode == "Full")
        {
            _logger.LogInformation("Full sync mode - ignoring last sync date");
            return DateTime.MinValue;
        }
        
        if (_options.Force)
        {
            return DateTime.MinValue;
        }

        if (_options.FromDate.HasValue)
        {
            return _options.FromDate.Value;
        }

        var syncState = await _dbContext.SyncStates
            .FirstOrDefaultAsync(s => s.CompanyName == _company.CompanyName && s.EntityType == "Product");

        return syncState?.LastSyncDateTime ?? DateTime.MinValue;
    }

    private string BuildProductQuery(DateTime lastSync)
    {
        var query = @"
            SELECT 
                T0.ItemCode, T0.ItemName, T0.ItemType, T0.InvntItem,
                T0.FrgnName, T0.CodeBars, T0.ValidFor,
                T0.SellItem, T0.PrchseItem,
                T0.ManBtchNum, T0.ManSerNum, T0.ManOutOnly,
                T0.QryGroup1, T0.QryGroup2, T0.QryGroup3, T0.QryGroup4,
                T0.SalUnitMsr, T0.BuyUnitMsr, T0.InvntryUom,
                T0.NumInSale, T0.NumInBuy, T0.PurPackMsr, T0.SalPackMsr,
                T0.PurPackUn, T0.SalPackUn,
                T0.SHeight1, T0.SHeight2, T0.SHght1Unit, T0.SHght2Unit,
                T0.SWidth1, T0.SWidth2, T0.SWdth1Unit, T0.SWdth2Unit,
                T0.SLength1, T0.SLength2, T0.SLen1Unit, T0.SLen2Unit,
                T0.SWeight1, T0.SWeight2, T0.SWght1Unit, T0.SWght2Unit,
                T0.SVolume, T0.SVolUnit,
                T0.BHeight1, T0.BHeight2, T0.BHght1Unit, T0.BHght2Unit,
                T0.BWidth1, T0.BWidth2, T0.BWdth1Unit, T0.BWdth2Unit,
                T0.BLength1, T0.BLength2, T0.BLen1Unit, T0.BLen2Unit,
                T0.BWeight1, T0.BWeight2, T0.BWght1Unit, T0.BWght2Unit,
                T0.BVolume, T0.BVolUnit,
                T0.OnHand, T0.IsCommited, T0.OnOrder,
                T0.UpdateDate, T0.CreateDate,
                T0.UserText, T0.PicturName, T0.AttachEntry,
                T0.ItmsGrpCod,
                T0.UgpEntry
            FROM OITM T0 
            WHERE T0.InvntItem = 'Y'";

        if (lastSync > DateTime.MinValue)
        {
            query += $" AND (UpdateDate >= '{lastSync:yyyy-MM-dd HH:mm:ss}' OR CreateDate >= '{lastSync:yyyy-MM-dd HH:mm:ss}')";
        }

        if (_options.ToDate.HasValue)
        {
            query += $" AND UpdateDate <= '{_options.ToDate.Value:yyyy-MM-dd HH:mm:ss}'";
        }
        
        // Add ORDER BY to ensure consistent results
        query += " ORDER BY T0.ItemCode";
        
        // Note: SAP Service Layer SQL endpoint may not support OFFSET/FETCH
        // We'll handle pagination in the result processing instead</        

        return query;
    }

    private async Task ProcessProduct(Dictionary<string, object> productData, Dictionary<int, string> itemGroupsMapping)
    {
        var itemCode = productData.GetValueOrDefault("ItemCode", "")?.ToString() ?? "";
        
        if (string.IsNullOrEmpty(itemCode))
        {
            _logger.LogWarning("Product missing ItemCode, skipping");
            return;
        }

        // Calculate hash for change detection
        var currentHash = SyncHelper.CalculateHash(productData);

        // Check if product has changed
        var syncStatus = await _dbContext.ProductSyncStatuses
            .FirstOrDefaultAsync(p => p.CompanyName == _company.CompanyName && p.ItemCode == itemCode);

        if (syncStatus != null && syncStatus.SyncHash == currentHash)
        {
            _logger.LogDebug("Product {ItemCode} unchanged, skipping", itemCode);
            return;
        }

        // Sync product image if available
        string? imageUrl = null;
        try
        {
            imageUrl = await SyncProductImageAsync(productData, syncStatus);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing image for product {ItemCode} - continuing with product sync", itemCode);
            // Don't fail the entire product sync if image sync fails
        }

        // Map to P4W format
        var p4wProduct = MapToP4WProduct(productData, itemGroupsMapping, imageUrl);
        
        // Validate required fields
        var validationErrors = ValidateP4WProduct(p4wProduct);
        if (validationErrors.Any())
        {
            _logger.LogWarning("Product {ItemCode} validation failed: {Errors}", 
                itemCode, string.Join(", ", validationErrors));
            
            // Log to database for review
            await LogValidationError(itemCode, validationErrors);
            return; // Skip this product
        }

        // Send to P4W API
        if (!_options.DryRun)
        {
            _logger.LogInformation("Syncing product {ItemCode} to P4W", itemCode);
            var success = await _p4wClient.UpsertProductAsync(p4wProduct);
            if (!success)
            {
                throw new Exception($"Failed to sync product {itemCode} to P4W");
            }
            
            // Update sync status immediately after successful P4W sync
            if (syncStatus == null)
            {
                syncStatus = new ProductSyncStatus
                {
                    CompanyName = _company.CompanyName,
                    ItemCode = itemCode,
                    CreatedDateTime = DateTime.UtcNow
                };
                _dbContext.ProductSyncStatuses.Add(syncStatus);
            }

            syncStatus.LastSyncDateTime = DateTime.UtcNow;
            syncStatus.SyncHash = currentHash;
            syncStatus.Status = "Success";
            syncStatus.SyncedToP4W = true;
            syncStatus.P4WSyncDateTime = DateTime.UtcNow;
            
            // Log the sync details
            _logger.LogInformation("Product {ItemCode} successfully synced to P4W and marked in database", itemCode);
        }
        else
        {
            _logger.LogInformation("[DRY RUN] Would sync product {ItemCode} to P4W", itemCode);
            
            // Even in dry run, update the record to show we processed it
            if (syncStatus == null)
            {
                syncStatus = new ProductSyncStatus
                {
                    CompanyName = _company.CompanyName,
                    ItemCode = itemCode,
                    CreatedDateTime = DateTime.UtcNow
                };
                _dbContext.ProductSyncStatuses.Add(syncStatus);
            }
            
            syncStatus.LastSyncDateTime = DateTime.UtcNow;
            syncStatus.SyncHash = currentHash;
            syncStatus.Status = "DryRun";
            syncStatus.SyncedToP4W = false;
        }
    }

    private Dictionary<string, object> MapToP4WProduct(Dictionary<string, object> sapProduct, Dictionary<int, string> itemGroupsMapping, string? imageUrl = null)
    {
        var itemCode = sapProduct.GetValueOrDefault("ItemCode", "")?.ToString() ?? "";
        var barcode = sapProduct.GetValueOrDefault("CodeBars", "")?.ToString() ?? "";
        
        // Start with required fields
        var productData = new Dictionary<string, object>
        {
            ["sku"] = itemCode,
            ["description"] = sapProduct.GetValueOrDefault("ItemName", "")?.ToString() ?? "",
            ["clientId"] = _clientId ?? throw new Exception("ClientId is missing"), // Rodion: Add client field - use fetched ID or fallback to name
            //["client"] = _clientId ?? _company.SapB1.ClientName // Add client field - use fetched ID or fallback to name
        };
        
        // Barcode/UPC handling
        if (!string.IsNullOrEmpty(barcode))
        {
            productData["upc"] = barcode;
            productData["barcodeValue"] = barcode;
            productData["barcodeType"] = DetectBarcodeTypeOnly(barcode);
        }
        else
        {
            // Use SKU as fallback
            productData["upc"] = itemCode;
            productData["barcodeValue"] = itemCode;
            productData["barcodeType"] = "Code128";
        }
        
        productData["referenceNumber"] = itemCode;
        
        // Control flags - only add if true
        if (sapProduct.GetValueOrDefault("ManBtchNum", "N")?.ToString() == "Y")
            productData["isLotControlled"] = true;
            
        if (sapProduct.GetValueOrDefault("ManSerNum", "N")?.ToString() == "Y")
            productData["isSerialControlled"] = true;
            
        if (sapProduct.GetValueOrDefault("ItemType", "I")?.ToString() == "P")
            productData["isBillOfMaterial"] = true;
            
        // Check for decimal control using QryGroup fields
        if (sapProduct.GetValueOrDefault("QryGroup1", "N")?.ToString() == "Y")
            productData["isDecimalControlled"] = true;
            
        // Pack size control
        var ugpEntry = sapProduct.GetValueOrDefault("UgpEntry", 0);
        if (Convert.ToInt32(ugpEntry) > 0)
        {
            productData["isPacksizeControlled"] = true;
            var packSizes = BuildPackSizesArray(sapProduct);
            if (packSizes.Any())
                productData["packsizes"] = packSizes;
        }
        
        // Dimensions - only add if > 0
        var height = GetDimension(sapProduct, "SHeight1", "BHeight1");
        var width = GetDimension(sapProduct, "SWidth1", "BWidth1");
        var length = GetDimension(sapProduct, "SLength1", "BLength1");
        var weight = GetDimension(sapProduct, "SWeight1", "BWeight1");
        
        if (height > 0) productData["height"] = height;
        if (width > 0) productData["width"] = width;
        if (length > 0) productData["length"] = length;
        if (weight > 0) productData["weight"] = weight;
        
        // Pallet configuration - only add if we have values
        var palletTie = GetDimensionInt(sapProduct, "PalletTie", 0);
        var palletHeight = GetDimensionInt(sapProduct, "PalletHeight", 0);
        if (palletTie > 0) productData["palletTie"] = palletTie;
        if (palletHeight > 0) productData["palletHeight"] = palletHeight;
        
        // Category from item group - lookup name from mapping
        var itemGroupCode = sapProduct.GetValueOrDefault("ItmsGrpCod");
        if (itemGroupCode != null && int.TryParse(itemGroupCode.ToString(), out int groupCode))
        {
            if (itemGroupsMapping.TryGetValue(groupCode, out string? groupName) && !string.IsNullOrEmpty(groupName))
            {
                productData["category"] = groupName;
            }
            else
            {
                // Fallback to code if name not found
                productData["category"] = groupCode.ToString();
            }
        }
            
        // Foreign name as commodity description
        var commodityDesc = sapProduct.GetValueOrDefault("FrgnName", "")?.ToString();
        if (!string.IsNullOrEmpty(commodityDesc))
            productData["commodityDescription"] = commodityDesc;
            
        // User text field might contain additional info
        var userText = sapProduct.GetValueOrDefault("UserText", "")?.ToString();
        if (!string.IsNullOrEmpty(userText))
        {
            // Could map to nmfc or other fields based on business rules
            productData["nmfc"] = userText;
        }
        
        // Add image URL if available
        if (!string.IsNullOrEmpty(imageUrl))
        {
            productData["imageUrl"] = imageUrl;
        }
        
        // Log the complete payload for debugging
        _logger.LogInformation("Complete product payload for {ItemCode}: {Payload}", 
            itemCode, JsonSerializer.Serialize(productData));
        
        return productData;
    }
    
    private int GetDimensionInt(Dictionary<string, object> product, string field, int defaultValue)
    {
        var value = product.GetValueOrDefault(field);
        if (value != null)
        {
            if (int.TryParse(value.ToString(), out int result))
                return result;
        }
        return defaultValue;
    }
    
    private string DetectBarcodeTypeOnly(string barcode)
    {
        if (string.IsNullOrEmpty(barcode))
            return "Code128";
            
        // Remove any spaces or dashes
        barcode = barcode.Replace(" ", "").Replace("-", "");
        
        // Detect based on length and format
        if (barcode.Length == 13 && barcode.All(char.IsDigit))
            return "EAN13";
        else if (barcode.Length == 12 && barcode.All(char.IsDigit))
            return "UPCA";
        else if (barcode.Length == 14 && barcode.All(char.IsDigit))
            return "ITF14";
        else
            return "Code128";
    }
    
    private (string barcodeType, string upc) DetectBarcodeTypeAndUPC(string barcode)
    {
        if (string.IsNullOrEmpty(barcode))
            return ("", "");
            
        // Remove any spaces or dashes
        barcode = barcode.Replace(" ", "").Replace("-", "");
        
        // Detect based on length and format
        if (barcode.Length == 13 && barcode.All(char.IsDigit))
        {
            // EAN-13 - extract UPC by removing check digit and country code
            var upc = barcode.Substring(1, 11); // Simple conversion, may need adjustment
            return ("EAN13", upc);
        }
        else if (barcode.Length == 12 && barcode.All(char.IsDigit))
        {
            // UPC-A
            return ("UPCA", barcode);
        }
        else if (barcode.Length == 14 && barcode.All(char.IsDigit))
        {
            // ITF-14
            return ("ITF14", barcode.Substring(1, 12));
        }
        else
        {
            // Default to Code128 for alphanumeric
            return ("Code128", barcode);
        }
    }
    
    private decimal GetDimension(Dictionary<string, object> product, string salesField, string purchaseField)
    {
        var value = product.GetValueOrDefault(salesField) ?? product.GetValueOrDefault(purchaseField) ?? 0;
        return Convert.ToDecimal(value);
    }
    
    private string ConvertToP4WUnit(string sapUnit, string type)
    {
        // Convert SAP units to P4W expected units
        // P4W uses specific capitalization
        return type switch
        {
            "length" => sapUnit.ToUpper() switch
            {
                "CM" => "Cm",
                "M" => "M",
                "MM" => "Mm",
                "FT" => "Ft",
                "IN" => "In",
                _ => "In"
            },
            "weight" => sapUnit.ToUpper() switch
            {
                "KG" => "Kg",
                "G" => "Gr",
                "OZ" => "Oz",
                "LB" => "Lb",
                "LBS" => "Lb",
                _ => "Lb"
            },
            "unit" => sapUnit.ToUpper() switch
            {
                // P4W UnitOfMeasure enum values - must match exactly
                "EA" => "EA",
                "EACH" => "EA",
                "PC" => "PC",
                "PCS" => "PC",
                "PK" => "PK",
                "PACK" => "PK",
                "CS" => "CS",
                "CASE" => "CS",
                "BX" => "BX",
                "BOX" => "BX",
                "PLT" => "PLT",
                "PALLET" => "PLT",
                "KG" => "KG",
                "LB" => "LB",
                "G" => "Gr",
                "GR" => "Gr",
                "OZ" => "Oz",
                _ => "EA" // Default to EA (Each)
            },
            _ => sapUnit
        };
    }
    
    private string DetermineFreightClass(decimal weight)
    {
        // Simple freight class determination based on weight
        // If no weight, use default Class100
        if (weight <= 0)
            return "Class100";
            
        return weight switch
        {
            < 1 => "Class400",
            < 5 => "Class250",
            < 10 => "Class150",
            < 20 => "Class125",
            < 30 => "Class100",
            < 50 => "Class85",
            < 70 => "Class70",
            < 100 => "Class60",
            _ => "Class50"
        };
    }
    
    /// <summary>
    /// Synchronizes product image from SAP to Azure Blob Storage
    /// </summary>
    /// <param name="productData">The product data from SAP</param>
    /// <param name="syncStatus">The current sync status record</param>
    /// <returns>The public URL of the uploaded image, or null if no image or upload failed</returns>
    private async Task<string?> SyncProductImageAsync(Dictionary<string, object> productData, ProductSyncStatus? syncStatus)
    {
        if (_azureBlobService == null)
        {
            _logger.LogDebug("Azure Blob Service not configured - skipping image sync");
            return syncStatus?.ImageUrl; // Return existing URL if any
        }

        var itemCode = productData.GetValueOrDefault("ItemCode", "")?.ToString() ?? "";
        
        // Check for attachment entry
        var attachmentEntry = productData.GetValueOrDefault("AttachEntry");
        var pictureName = productData.GetValueOrDefault("PicturName", "")?.ToString();
        
        int? attachmentId = null;
        if (attachmentEntry != null && int.TryParse(attachmentEntry.ToString(), out int id) && id > 0)
        {
            attachmentId = id;
        }
        
        // If no attachment, return existing URL or null
        if (!attachmentId.HasValue && string.IsNullOrEmpty(pictureName))
        {
            _logger.LogDebug("Product {ItemCode} has no attachment or picture", itemCode);
            return syncStatus?.ImageUrl;
        }

        Stream? imageStream = null;
        try
        {
            string? fileName = null;
            
            // Try to get attachment via AttachEntry first
            if (attachmentId.HasValue)
            {
                _logger.LogDebug("Fetching attachment {AttachmentId} for product {ItemCode}", attachmentId.Value, itemCode);
                
                // Get attachment metadata
                var attachmentDetails = await _serviceLayer.GetAttachmentAsync(attachmentId.Value);
                if (attachmentDetails != null)
                {
                    fileName = attachmentDetails.GetValueOrDefault("FileName", "")?.ToString();
                    var filePath = attachmentDetails.GetValueOrDefault("TargetPath", "")?.ToString();
                    
                    if (!string.IsNullOrEmpty(filePath))
                    {
                        imageStream = await _serviceLayer.DownloadAttachmentFileAsync(filePath);
                    }
                }
            }
            
            // If no stream from attachment, try alternative methods
            if (imageStream == null && !string.IsNullOrEmpty(pictureName))
            {
                _logger.LogDebug("Attempting to fetch image via PicturName {PictureName} for product {ItemCode}", pictureName, itemCode);
                // This might require different API calls depending on SAP configuration
                // For now, log that we found a picture name but couldn't retrieve it
                _logger.LogInformation("Product {ItemCode} has PicturName {PictureName} but image retrieval not implemented for this method", 
                    itemCode, pictureName);
            }
            
            if (imageStream == null)
            {
                _logger.LogDebug("Could not retrieve image stream for product {ItemCode}", itemCode);
                return syncStatus?.ImageUrl;
            }

            // Calculate hash of the image to detect changes
            var imageHash = await AzureBlobService.CalculateHashAsync(imageStream);
            
            // Check if image has changed
            if (syncStatus?.ImageHash == imageHash && !string.IsNullOrEmpty(syncStatus.ImageUrl))
            {
                _logger.LogDebug("Image hash unchanged for product {ItemCode}, using existing URL", itemCode);
                return syncStatus.ImageUrl;
            }

            // Generate blob name
            var fileExtension = !string.IsNullOrEmpty(fileName) ? Path.GetExtension(fileName) : ".jpg";
            if (string.IsNullOrEmpty(fileExtension))
            {
                fileExtension = ".jpg"; // Default extension
            }
            
            var blobName = AzureBlobService.GenerateBlobName(_company.CompanyName, itemCode, imageHash, fileExtension);
            
            // Check if blob already exists
            if (await _azureBlobService.BlobExistsAsync(blobName))
            {
                var existingUrl = _azureBlobService.GetBlobUrl(blobName);
                _logger.LogDebug("Image already exists in blob storage for product {ItemCode}, URL: {Url}", itemCode, existingUrl);
                
                // Update sync status with existing image info
                if (syncStatus != null)
                {
                    syncStatus.ImageUrl = existingUrl;
                    syncStatus.ImageHash = imageHash;
                    syncStatus.ImageSyncDateTime = DateTime.UtcNow;
                }
                
                return existingUrl;
            }

            // Upload image to Azure Blob Storage
            _logger.LogInformation("Uploading image for product {ItemCode} to Azure Blob Storage", itemCode);
            var uploadedUrl = await _azureBlobService.UploadImageAsync(imageStream, blobName);
            
            if (!string.IsNullOrEmpty(uploadedUrl))
            {
                _logger.LogInformation("Successfully uploaded image for product {ItemCode}, URL: {Url}", itemCode, uploadedUrl);
                
                // Update sync status with new image info
                if (syncStatus != null)
                {
                    syncStatus.ImageUrl = uploadedUrl;
                    syncStatus.ImageHash = imageHash;
                    syncStatus.ImageSyncDateTime = DateTime.UtcNow;
                }
                
                return uploadedUrl;
            }
            else
            {
                _logger.LogError("Failed to upload image for product {ItemCode}", itemCode);
                return syncStatus?.ImageUrl; // Return existing URL if upload failed
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing image for product {ItemCode}", itemCode);
            return syncStatus?.ImageUrl; // Return existing URL on error
        }
        finally
        {
            // Dispose of the stream if we created it
            if (imageStream != null)
            {
                await imageStream.DisposeAsync();
            }
        }
    }
    
    private List<object> BuildPackSizesArray(Dictionary<string, object> product)
    {
        var packSizes = new List<object>();
        
        // Get pack units from product
        var salPackUn = Convert.ToDecimal(product.GetValueOrDefault("SalPackUn", 1));
        var purPackUn = Convert.ToDecimal(product.GetValueOrDefault("PurPackUn", 1));
        
        // Add sales pack if different from 1
        if (salPackUn > 1)
        {
            packSizes.Add(new Dictionary<string, object>
            {
                ["name"] = "Case",
                ["eachCount"] = salPackUn,
                ["barcodeValue"] = "", // TODO: Get case barcode from OBCD
                ["barcodeType"] = "Code128",
                ["height"] = GetDimension(product, "SHeight2", "BHeight2"),
                ["width"] = GetDimension(product, "SWidth2", "BWidth2"),
                ["length"] = GetDimension(product, "SLength2", "BLength2"),
                ["weight"] = GetDimension(product, "SWeight2", "BWeight2"),
                ["palletTie"] = 8,
                ["palletHeight"] = 4
            });
        }
        
        // TODO: Query UGP1 table for additional pack sizes
        
        return packSizes;
    }


    private List<string> ValidateP4WProduct(Dictionary<string, object> product)
    {
        var errors = new List<string>();
        
        // Required fields validation
        if (string.IsNullOrEmpty(product.GetValueOrDefault("sku", "")?.ToString()))
            errors.Add("SKU is required");
            
        if (string.IsNullOrEmpty(product.GetValueOrDefault("description", "")?.ToString()))
            errors.Add("Description is required");
        
        // Validate barcode format if present
        var barcode = product.GetValueOrDefault("barcodeValue", "")?.ToString();
        var barcodeType = product.GetValueOrDefault("barcodeType", "")?.ToString();
        
        if (!string.IsNullOrEmpty(barcode) && !string.IsNullOrEmpty(barcodeType))
        {
            if (!ValidateBarcodeFormat(barcode, barcodeType))
                errors.Add($"Invalid barcode format for type {barcodeType}");
        }
        
        // Weight validation removed - products should still sync even without weight
        // Freight class will use default if no weight available
        
        // Validate pack sizes if controlled
        var isPackSizeControlled = Convert.ToBoolean(product.GetValueOrDefault("isPacksizeControlled", false));
        if (isPackSizeControlled)
        {
            var packSizes = product.GetValueOrDefault("packsizes") as List<object>;
            if (packSizes == null || !packSizes.Any())
                errors.Add("Pack sizes array is required when pack size controlled");
        }
        
        return errors;
    }
    
    private bool ValidateBarcodeFormat(string barcode, string barcodeType)
    {
        // Remove spaces and dashes for validation
        barcode = barcode.Replace(" ", "").Replace("-", "");
        
        return barcodeType switch
        {
            "EAN13" => barcode.Length == 13 && barcode.All(char.IsDigit),
            "UPCA" => barcode.Length == 12 && barcode.All(char.IsDigit),
            "ITF14" => barcode.Length == 14 && barcode.All(char.IsDigit),
            "Code128" => !string.IsNullOrEmpty(barcode) && barcode.Length <= 48,
            _ => true // Unknown type, allow it
        };
    }
    
    private async Task LogValidationError(string itemCode, List<string> errors)
    {
        var log = new IntegrationLog
        {
            CompanyName = _company.CompanyName,
            Operation = "ProductSync.Validation",
            Level = "Warning",
            Message = $"Product {itemCode} validation failed: {string.Join("; ", errors)}",
            Timestamp = DateTime.UtcNow,
            CorrelationId = Guid.NewGuid(),
            ExecutionId = Guid.NewGuid()
        };
        
        _dbContext.IntegrationLogs.Add(log);
        await _dbContext.SaveChangesAsync();
    }

    private async Task UpdateSyncState()
    {
        var syncState = await _dbContext.SyncStates
            .FirstOrDefaultAsync(s => s.CompanyName == _company.CompanyName && s.EntityType == "Product");

        if (syncState == null)
        {
            syncState = new SyncState
            {
                CompanyName = _company.CompanyName,
                EntityType = "Product"
            };
            _dbContext.SyncStates.Add(syncState);
        }

        syncState.LastSyncDateTime = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
    }
    
    private async Task<List<Dictionary<string, object>>> FetchAllProductsWithPagination(DateTime lastSync)
    {
        var allProducts = new List<Dictionary<string, object>>();
        var pageSize = 20; // OData page size - Service Layer enforces this limit
        var skip = 0;
        var hasMorePages = true;
        var pageCount = 0;
        
        _logger.LogInformation("Fetching products from Service Layer using OData pagination");
        
        try
        {
            while (hasMorePages)
            {
                pageCount++;
                
                // Build OData query with pagination
                // Note: Some fields like QryGroup1-4 may not be available via OData
                var endpoint = $"Items?$select=ItemCode,ItemName,ItemType,InventoryItem,ForeignName,BarCode,Valid,SalesItem,PurchaseItem,ManageBatchNumbers,ManageSerialNumbers,SalesUnit,PurchaseUnit,InventoryUOM,ItemsGroupCode,User_Text,Picture,AttachmentEntry,UpdateDate,CreateDate&$orderby=ItemCode&$skip={skip}&$top={pageSize}";
                
                // Add filter only if we want inventory items
                // For now, get all items to test pagination
                if (lastSync > DateTime.MinValue)
                {
                    var lastSyncStr = lastSync.ToString("yyyy-MM-ddTHH:mm:ss");
                    var filter = $"UpdateDate ge datetime'{lastSyncStr}' or CreateDate ge datetime'{lastSyncStr}'";
                    endpoint = $"Items?$filter={Uri.EscapeDataString(filter)}&$select=ItemCode,ItemName,ItemType,InventoryItem,ForeignName,BarCode,Valid,SalesItem,PurchaseItem,ManageBatchNumbers,ManageSerialNumbers,SalesUnit,PurchaseUnit,InventoryUOM,ItemsGroupCode,User_Text,Picture,AttachmentEntry,UpdateDate,CreateDate&$orderby=ItemCode&$skip={skip}&$top={pageSize}";
                }
                
                _logger.LogDebug("Fetching products page {Page} (skip={Skip}, top={Top})", pageCount, skip, pageSize);
                
                var response = await _serviceLayer.GetAsync<Dictionary<string, object>>(endpoint);
                
                if (response != null && response.ContainsKey("value"))
                {
                    var items = response["value"];
                    
                    if (items is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        var pageProducts = new List<Dictionary<string, object>>();
                        
                        foreach (var item in jsonElement.EnumerateArray())
                        {
                            var product = new Dictionary<string, object>();
                            
                            // Map OData fields to SQL field names for compatibility
                            foreach (var prop in item.EnumerateObject())
                            {
                                var fieldName = MapODataFieldToSqlField(prop.Name);
                                product[fieldName] = GetJsonValue(prop.Value) ?? DBNull.Value;
                            }
                            
                            pageProducts.Add(product);
                        }
                        
                        allProducts.AddRange(pageProducts);
                        _logger.LogInformation("Fetched {Count} products in page {Page} (total: {Total})", 
                            pageProducts.Count, pageCount, allProducts.Count);
                        
                        // Check if there are more pages
                        hasMorePages = pageProducts.Count == pageSize;
                        skip += pageSize;
                        
                        // If user specified a limit and we've reached it, stop
                        if (_options.Limit.HasValue && allProducts.Count >= _options.Limit.Value)
                        {
                            hasMorePages = false;
                            _logger.LogInformation("Reached user-specified limit of {Limit} products", _options.Limit.Value);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Unexpected response format for Items endpoint");
                        hasMorePages = false;
                    }
                }
                else
                {
                    _logger.LogWarning("No value property in Items response");
                    hasMorePages = false;
                }
            }
            
            _logger.LogInformation("Successfully fetched {Total} products across {Pages} page(s)", 
                allProducts.Count, pageCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching products with pagination");
            // Return what we have so far
            if (allProducts.Count > 0)
            {
                _logger.LogWarning("Returning partial results: {Count} products", allProducts.Count);
            }
        }
        
        return allProducts;
    }
    
    private string MapODataFieldToSqlField(string odataField)
    {
        // Map OData field names to SQL field names for compatibility with existing code
        return odataField switch
        {
            "ItemCode" => "ItemCode",
            "ItemName" => "ItemName",
            "ItemType" => "ItemType",
            "InventoryItem" => "InvntItem",
            "ForeignName" => "FrgnName",
            "BarCode" => "CodeBars",
            "Valid" => "ValidFor",
            "SalesItem" => "SellItem",
            "PurchaseItem" => "PrchseItem",
            "ManageBatchNumbers" => "ManBtchNum",
            "ManageSerialNumbers" => "ManSerNum",
            "SalesUnit" => "SalUnitMsr",
            "PurchaseUnit" => "BuyUnitMsr",
            "InventoryUOM" => "InvntryUom",
            "PurchasePackagingUnit" => "PurPackUn",
            "SalesPackagingUnit" => "SalPackUn",
            "ItemsGroupCode" => "ItmsGrpCod",
            "User_Text" => "UserText",
            "Picture" => "PicturName",
            "AttachmentEntry" => "AttachEntry",
            _ => odataField
        };
    }
    
    private object? GetJsonValue(System.Text.Json.JsonElement element)
    {
        return element.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => element.GetString(),
            System.Text.Json.JsonValueKind.Number => element.TryGetInt32(out var intVal) ? intVal : element.GetDouble(),
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            System.Text.Json.JsonValueKind.Null => null,
            _ => element.ToString()
        };
    }
    
    private async Task<Dictionary<int, string>> FetchItemGroupsMapping()
    {
        var mapping = new Dictionary<int, string>();
        var pageCount = 0;
        
        try
        {
            _logger.LogInformation("Fetching ItemGroups from Service Layer API");
            
            // Start with the first page
            var endpoint = "ItemGroups?$select=Number,GroupName";
            
            while (!string.IsNullOrEmpty(endpoint))
            {
                pageCount++;
                _logger.LogDebug("Fetching ItemGroups page {Page}", pageCount);
                
                var response = await _serviceLayer.GetAsync<Dictionary<string, object>>(endpoint);
                
                if (response != null)
                {
                    _logger.LogDebug("ItemGroups response received: {Keys}", string.Join(", ", response.Keys));
                    
                    if (response.ContainsKey("value"))
                    {
                        var itemGroups = response["value"];
                        _logger.LogDebug("ItemGroups value type: {Type}", itemGroups?.GetType().Name ?? "null");
                        
                        // Handle the response which could be a JSON array
                        if (itemGroups is System.Text.Json.JsonElement jsonElement)
                        {
                            if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                            {
                                foreach (var item in jsonElement.EnumerateArray())
                                {
                                    try
                                    {
                                        var number = item.GetProperty("Number").GetInt32();
                                        var groupName = item.GetProperty("GroupName").GetString();
                                        
                                        if (!string.IsNullOrEmpty(groupName))
                                        {
                                            mapping[number] = groupName;
                                            _logger.LogDebug("Mapped ItemGroup {Code} to {Name}", number, groupName);
                                        }
                                    }
                                    catch (Exception itemEx)
                                    {
                                        _logger.LogDebug(itemEx, "Failed to parse ItemGroup item");
                                    }
                                }
                            }
                        }
                        else if (itemGroups is List<object> itemGroupsList)
                        {
                            foreach (var item in itemGroupsList)
                            {
                                if (item is Dictionary<string, object> group)
                                {
                                    var number = group.GetValueOrDefault("Number");
                                    var groupName = group.GetValueOrDefault("GroupName")?.ToString();
                                    
                                    if (number != null && int.TryParse(number.ToString(), out int groupCode) && !string.IsNullOrEmpty(groupName))
                                    {
                                        mapping[groupCode] = groupName;
                                        _logger.LogDebug("Mapped ItemGroup {Code} to {Name}", groupCode, groupName);
                                    }
                                }
                            }
                        }
                    }
                    
                    // Check for next page link
                    if (response.ContainsKey("@odata.nextLink"))
                    {
                        var nextLink = response["@odata.nextLink"];
                        if (nextLink is System.Text.Json.JsonElement nextLinkJson)
                        {
                            var nextUrl = nextLinkJson.GetString();
                            if (!string.IsNullOrEmpty(nextUrl))
                            {
                                // Extract the relative URL from the full URL
                                try
                                {
                                    var uri = new Uri(nextUrl);
                                    endpoint = uri.PathAndQuery.TrimStart('/');
                                    // Remove the base path if present
                                    if (endpoint.StartsWith("b1s/v2/"))
                                    {
                                        endpoint = endpoint.Substring(7); // Remove "b1s/v2/"
                                    }
                                    else if (endpoint.StartsWith("su11/b1s/v2/"))
                                    {
                                        endpoint = endpoint.Substring(13); // Remove "su11/b1s/v2/"
                                    }
                                    _logger.LogDebug("Found next page: {NextLink}", endpoint);
                                }
                                catch (UriFormatException)
                                {
                                    // It might be a relative URL already
                                    endpoint = nextUrl;
                                    if (endpoint.StartsWith("/"))
                                    {
                                        endpoint = endpoint.TrimStart('/');
                                    }
                                    _logger.LogDebug("Using relative next page: {NextLink}", endpoint);
                                }
                            }
                            else
                            {
                                endpoint = null; // No more pages
                            }
                        }
                        else
                        {
                            endpoint = null; // No more pages
                        }
                    }
                    else
                    {
                        _logger.LogDebug("No more pages - @odata.nextLink not found");
                        endpoint = null; // No more pages
                    }
                }
                else
                {
                    _logger.LogWarning("ItemGroups response was null for page {Page}", pageCount);
                    break;
                }
            }
            
            _logger.LogInformation("Successfully loaded {Count} item groups from {Pages} page(s)", mapping.Count, pageCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while fetching ItemGroups from Service Layer on page {Page}", pageCount);
            // Return what we have so far rather than nothing
            if (mapping.Count > 0)
            {
                _logger.LogWarning("Partial ItemGroups loaded: {Count} groups. Will use these and fallback to codes for missing ones.", mapping.Count);
            }
            else
            {
                _logger.LogWarning("Failed to fetch any ItemGroups from Service Layer, will use item group codes as fallback");
            }
        }
        
        return mapping;
    }
    
    public async Task<(int totalProducts, int syncedProducts, DateTime? lastSyncTime)> GetSyncStatusAsync()
    {
        var stats = await _dbContext.ProductSyncStatuses
            .Where(p => p.CompanyName == _company.CompanyName)
            .GroupBy(p => p.CompanyName)
            .Select(g => new
            {
                Total = g.Count(),
                Synced = g.Count(p => p.SyncedToP4W),
                LastSync = g.Max(p => p.P4WSyncDateTime)
            })
            .FirstOrDefaultAsync();
            
        if (stats == null)
            return (0, 0, null);
            
        return (stats.Total, stats.Synced, stats.LastSync);
    }
}
