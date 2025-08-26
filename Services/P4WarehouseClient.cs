using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace P4WIntegration.Services;

public class P4WarehouseClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<P4WarehouseClient> _logger;
    private readonly string _apiKey;
    private readonly string _clientName;
    private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;
    private readonly string _baseUrl = "https://api.p4warehouse.com/";

    public P4WarehouseClient(
        string apiKey,
        string clientName,
        ILogger<P4WarehouseClient> logger)
    {
        _apiKey = apiKey;
        _clientName = clientName;
        _logger = logger;

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_baseUrl),
            Timeout = TimeSpan.FromMinutes(2)
        };

        _httpClient.DefaultRequestHeaders.Add("ApiKey", _apiKey);
        // Try adding Client header in case it's needed
        _httpClient.DefaultRequestHeaders.Add("Client", _clientName);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _retryPolicy = Policy
            .HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
            .WaitAndRetryAsync(
                3,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    _logger.LogWarning("P4W API retry {RetryCount} after {Timespan}s", retryCount, timespan.TotalSeconds);
                });
    }

    // Client Operations
    public async Task<string?> GetClientIdAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("clients");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var clients = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(json);
                if (clients != null && clients.Count > 0)
                {
                    // Return the first client's ID
                    if (clients[0].TryGetValue("id", out var clientId))
                    {
                        _logger.LogInformation("Found client ID: {ClientId}", clientId);
                        return clientId?.ToString();
                    }
                }
            }
            _logger.LogWarning("Could not retrieve client ID from P4W API");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching client ID");
            return null;
        }
    }
    
    // Product Operations
    public async Task<bool> CheckProductExistsAsync(string sku)
    {
        try
        {
            // Check if product exists by SKU
            var response = await _httpClient.GetAsync($"products?Sku={Uri.EscapeDataString(sku)}");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var products = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(json);
                return products != null && products.Count > 0;
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if product exists: {Sku}", sku);
            return false;
        }
    }
    
    public async Task<bool> UpsertProductAsync(Dictionary<string, object> product)
    {
        try
        {
            var sku = product.GetValueOrDefault("sku")?.ToString() ?? "";
            var json = JsonSerializer.Serialize(product);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogDebug("Sending product to P4W API: {Url}", $"{_baseUrl}products");
            _logger.LogDebug("Product payload: {Payload}", json);
            _logger.LogDebug("Headers: ApiKey={ApiKey}", 
                _apiKey.Substring(0, 10) + "...");
            
            // Check if product exists to determine POST vs PUT
            var exists = await CheckProductExistsAsync(sku);
            
            HttpResponseMessage response;
            if (exists)
            {
                _logger.LogInformation("Product {Sku} exists, using PUT to update", sku);
                response = await _retryPolicy.ExecuteAsync(async () =>
                    await _httpClient.PutAsync("products", content));
            }
            else
            {
                _logger.LogInformation("Product {Sku} doesn't exist, using POST to create", sku);
                response = await _retryPolicy.ExecuteAsync(async () =>
                    await _httpClient.PostAsync("products", content));
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            _logger.LogDebug("P4W API Response: {StatusCode}, Body: {ResponseBody}", response.StatusCode, responseContent);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Product {Sku} upserted successfully to P4W", product.GetValueOrDefault("sku", "Unknown"));
                return true;
            }

            _logger.LogError("Failed to upsert product {Sku} to P4W. Status: {StatusCode}, Error: {Error}", 
                product.GetValueOrDefault("sku", "Unknown"), response.StatusCode, responseContent);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception upserting product {Sku}", product.GetValueOrDefault("sku", "Unknown"));
            return false;
        }
    }

    public async Task<bool> UpsertProductBatchAsync(List<Dictionary<string, object>> products)
    {
        // P4W doesn't have a batch endpoint, process individually
        var success = true;
        foreach (var product in products)
        {
            if (!await UpsertProductAsync(product))
            {
                success = false;
            }
        }
        return success;
    }

    // Customer Operations
    public async Task<bool> UpsertCustomerAsync(Dictionary<string, object> customer)
    {
        try
        {
            var json = JsonSerializer.Serialize(customer);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogDebug("Sending customer to P4W API: {Url}", $"{_baseUrl}customers");
            _logger.LogDebug("Customer payload: {Payload}", json);
            
            var response = await _retryPolicy.ExecuteAsync(async () =>
                await _httpClient.PostAsync("customers", content));

            var responseContent = await response.Content.ReadAsStringAsync();
            _logger.LogDebug("P4W API Response: {StatusCode}, Body: {ResponseBody}", response.StatusCode, responseContent);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Customer {CardCode} upserted successfully to P4W", customer["CardCode"]);
                return true;
            }

            _logger.LogError("Failed to upsert customer {CardCode} to P4W. Status: {StatusCode}, Error: {Error}", 
                customer["CardCode"], response.StatusCode, responseContent);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception upserting customer {CardCode}", customer["CardCode"]);
            return false;
        }
    }

    public async Task<bool> UpsertCustomerBatchAsync(List<Dictionary<string, object>> customers)
    {
        try
        {
            var json = JsonSerializer.Serialize(customers);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _retryPolicy.ExecuteAsync(async () =>
                await _httpClient.PostAsync($"customers/batch", content));

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Batch of {Count} customers upserted successfully", customers.Count);
                return true;
            }

            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Failed to upsert customer batch: {Error}", error);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception upserting customer batch");
            return false;
        }
    }

    // Vendor Operations
    public async Task<bool> UpsertVendorAsync(Dictionary<string, object> vendor)
    {
        try
        {
            var json = JsonSerializer.Serialize(vendor);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _retryPolicy.ExecuteAsync(async () =>
                await _httpClient.PostAsync("vendors", content));

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Vendor {CardCode} upserted successfully", vendor["CardCode"]);
                return true;
            }

            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Failed to upsert vendor {CardCode}: {Error}", vendor["CardCode"], error);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception upserting vendor {CardCode}", vendor["CardCode"]);
            return false;
        }
    }

    // Purchase Order Operations
    public async Task<bool> CreatePurchaseOrderAsync(Dictionary<string, object> purchaseOrder)
    {
        try
        {
            var json = JsonSerializer.Serialize(purchaseOrder);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _retryPolicy.ExecuteAsync(async () =>
                await _httpClient.PostAsync($"purchase-orders", content));

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Purchase Order {DocNum} created successfully", purchaseOrder["DocNum"]);
                return true;
            }

            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Failed to create purchase order {DocNum}: {Error}", purchaseOrder["DocNum"], error);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception creating purchase order {DocNum}", purchaseOrder["DocNum"]);
            return false;
        }
    }

    // Sales Order / Pick Ticket Operations
    public async Task<bool> CreatePickTicketAsync(Dictionary<string, object> pickTicket)
    {
        try
        {
            var json = JsonSerializer.Serialize(pickTicket);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _retryPolicy.ExecuteAsync(async () =>
                await _httpClient.PostAsync($"pick-tickets", content));

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Pick Ticket for SO {DocNum} created successfully", pickTicket["DocNum"]);
                return true;
            }

            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Failed to create pick ticket for SO {DocNum}: {Error}", pickTicket["DocNum"], error);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception creating pick ticket for SO {DocNum}", pickTicket["DocNum"]);
            return false;
        }
    }

    // Get Operations for Uploads
    public async Task<List<Dictionary<string, object>>?> GetCompletedGoodsReceiptsAsync(DateTime since)
    {
        try
        {
            var response = await _httpClient.GetAsync($"goods-receipts/completed?since={since:yyyy-MM-ddTHH:mm:ss}");
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<Dictionary<string, object>>>(json);
            }

            _logger.LogError("Failed to get completed goods receipts: {Status}", response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception getting completed goods receipts");
            return null;
        }
    }

    public async Task<List<Dictionary<string, object>>?> GetCompletedDeliveriesAsync(DateTime since)
    {
        try
        {
            var response = await _httpClient.GetAsync($"deliveries/completed?since={since:yyyy-MM-ddTHH:mm:ss}");
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<Dictionary<string, object>>>(json);
            }

            _logger.LogError("Failed to get completed deliveries: {Status}", response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception getting completed deliveries");
            return null;
        }
    }

    public async Task<bool> MarkGoodsReceiptAsProcessedAsync(string receiptId)
    {
        try
        {
            var response = await _httpClient.PatchAsync($"goods-receipts/{receiptId}/processed", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception marking goods receipt {ReceiptId} as processed", receiptId);
            return false;
        }
    }

    public async Task<bool> MarkDeliveryAsProcessedAsync(string deliveryId)
    {
        try
        {
            var response = await _httpClient.PatchAsync($"deliveries/{deliveryId}/processed", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception marking delivery {DeliveryId} as processed", deliveryId);
            return false;
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}