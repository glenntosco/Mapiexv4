using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace P4WIntegration.Services;

public class ServiceLayerClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ServiceLayerClient> _logger;
    private readonly string _serviceLayerUrl;
    private readonly string _companyDb;
    private readonly string _userName;
    private readonly string _password;
    private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;
    private string? _sessionId;
    private string? _routeId;
    private readonly CookieContainer _cookieContainer;
    private DateTime _lastLoginTime = DateTime.MinValue;
    private readonly TimeSpan _sessionTimeout = TimeSpan.FromMinutes(25);

    public ServiceLayerClient(
        string serviceLayerUrl,
        string companyDb,
        string userName,
        string password,
        ILogger<ServiceLayerClient> logger)
    {
        _serviceLayerUrl = serviceLayerUrl.TrimEnd('/');
        _companyDb = companyDb;
        _userName = userName;
        _password = password;
        _logger = logger;

        _cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = _cookieContainer,
            ServerCertificateCustomValidationCallback = (sender, cert, chain, errors) => true
        };

        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(_serviceLayerUrl.EndsWith("/") ? _serviceLayerUrl : _serviceLayerUrl + "/"),
            Timeout = TimeSpan.FromMinutes(5)
        };

        _retryPolicy = Policy
            .HandleResult<HttpResponseMessage>(r => 
                r.StatusCode == HttpStatusCode.Unauthorized ||
                r.StatusCode == HttpStatusCode.ServiceUnavailable ||
                r.StatusCode == HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(
                3,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: async (outcome, timespan, retryCount, context) =>
                {
                    if (outcome.Result?.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        _logger.LogWarning("Unauthorized response, attempting re-login (retry {RetryCount})", retryCount);
                        await LoginAsync();
                    }
                    else
                    {
                        _logger.LogWarning("Retrying after {Timespan}s (retry {RetryCount})", timespan.TotalSeconds, retryCount);
                    }
                });
    }

    public async Task<bool> LoginAsync()
    {
        try
        {
            _logger.LogInformation("Logging into Service Layer for company {CompanyDb}", _companyDb);
            _logger.LogDebug("Service Layer Base URL: {BaseUrl}", _serviceLayerUrl);
            _logger.LogDebug("Full Login URL: {LoginUrl}", $"{_serviceLayerUrl}/Login");

            var loginData = new
            {
                CompanyDB = _companyDb,
                UserName = _userName,
                Password = _password
            };

            var json = JsonSerializer.Serialize(loginData);
            _logger.LogDebug("Login payload: {Payload}", json);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("Login", content);

            if (response.IsSuccessStatusCode)
            {
                // Extract session cookies
                var cookies = _cookieContainer.GetCookies(new Uri(_serviceLayerUrl));
                _sessionId = cookies["B1SESSION"]?.Value;
                _routeId = cookies["ROUTEID"]?.Value;
                _lastLoginTime = DateTime.UtcNow;

                _logger.LogInformation("Successfully logged into Service Layer");
                return true;
            }

            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Login failed: {StatusCode} - {Error}", response.StatusCode, error);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during Service Layer login");
            return false;
        }
    }

    private async Task EnsureAuthenticatedAsync()
    {
        if (string.IsNullOrEmpty(_sessionId) || 
            DateTime.UtcNow - _lastLoginTime > _sessionTimeout)
        {
            await LoginAsync();
        }
    }

    public async Task<T?> ExecuteSqlQueryAsync<T>(string sqlQuery)
    {
        await EnsureAuthenticatedAsync();

        // Generate a unique query code based on hash of the query
        var queryHash = System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(sqlQuery));
        var queryCode = "P4W_" + BitConverter.ToString(queryHash).Replace("-", "").Substring(0, 8);
        
        // First try to execute existing stored query using the List endpoint
        var executeUrl = $"SQLQueries('{queryCode}')/List";
        var emptyContent = new StringContent("{}", Encoding.UTF8, "application/json");
        
        var response = await _httpClient.PostAsync(executeUrl, emptyContent);
        
        // If query doesn't exist, create it first
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var createData = new { 
                SqlCode = queryCode,
                SqlText = sqlQuery,
                SqlName = $"P4W Integration Query {queryCode}"
            };
            var createJson = JsonSerializer.Serialize(createData);
            var createContent = new StringContent(createJson, Encoding.UTF8, "application/json");
            
            var createResponse = await _httpClient.PostAsync("SQLQueries", createContent);
            if (!createResponse.IsSuccessStatusCode && createResponse.StatusCode != HttpStatusCode.Conflict)
            {
                var createError = await createResponse.Content.ReadAsStringAsync();
                _logger.LogError("Failed to create SQL query: {StatusCode} - {Error}", createResponse.StatusCode, createError);
                return default;
            }
            
            // Now execute the created query using the List endpoint
            response = await _httpClient.PostAsync(executeUrl, emptyContent);
        }

        if (response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }

        var error = await response.Content.ReadAsStringAsync();
        _logger.LogError("SQL Query failed: {StatusCode} - {Error}", response.StatusCode, error);
        return default;
    }

    public async Task<List<Dictionary<string, object>>?> ExecuteSqlQueryAsync(string sqlQuery)
    {
        await EnsureAuthenticatedAsync();

        // Generate a unique query code based on hash of the query
        var queryHash = System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(sqlQuery));
        var queryCode = "P4W_" + BitConverter.ToString(queryHash).Replace("-", "").Substring(0, 8);
        
        // First try to execute existing stored query using the List endpoint
        var executeUrl = $"SQLQueries('{queryCode}')/List";
        var emptyContent = new StringContent("{}", Encoding.UTF8, "application/json");
        
        var response = await _httpClient.PostAsync(executeUrl, emptyContent);
        
        // If query doesn't exist, create it first
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var createData = new { 
                SqlCode = queryCode,
                SqlText = sqlQuery,
                SqlName = $"P4W Integration Query {queryCode}"
            };
            var createJson = JsonSerializer.Serialize(createData);
            var createContent = new StringContent(createJson, Encoding.UTF8, "application/json");
            
            var createResponse = await _httpClient.PostAsync("SQLQueries", createContent);
            if (!createResponse.IsSuccessStatusCode && createResponse.StatusCode != HttpStatusCode.Conflict)
            {
                var createError = await createResponse.Content.ReadAsStringAsync();
                _logger.LogError("Failed to create SQL query: {StatusCode} - {Error}", createResponse.StatusCode, createError);
                return default;
            }
            
            // Now execute the created query using the List endpoint
            response = await _httpClient.PostAsync(executeUrl, emptyContent);
        }

        if (response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseContent);
            
            if (result != null && result.ContainsKey("value"))
            {
                var valueArray = result["value"].EnumerateArray();
                var list = new List<Dictionary<string, object>>();
                
                foreach (var item in valueArray)
                {
                    var dict = new Dictionary<string, object>();
                    foreach (var prop in item.EnumerateObject())
                    {
                        dict[prop.Name] = prop.Value.ValueKind switch
                        {
                            JsonValueKind.String => prop.Value.GetString() ?? string.Empty,
                            JsonValueKind.Number => prop.Value.GetDecimal(),
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            JsonValueKind.Null => null!,
                            _ => prop.Value.ToString()
                        };
                    }
                    list.Add(dict);
                }
                return list;
            }
        }

        var error = await response.Content.ReadAsStringAsync();
        _logger.LogError("SQL Query failed: {StatusCode} - {Error}", response.StatusCode, error);
        return null;
    }

    public async Task<T?> GetAsync<T>(string endpoint)
    {
        await EnsureAuthenticatedAsync();

        var response = await _retryPolicy.ExecuteAsync(async () =>
            await _httpClient.GetAsync($"{endpoint}"));

        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }

        var error = await response.Content.ReadAsStringAsync();
        _logger.LogError("GET request failed: {StatusCode} - {Error}", response.StatusCode, error);
        return default;
    }

    public async Task<T?> PostAsync<T>(string endpoint, object data)
    {
        await EnsureAuthenticatedAsync();

        var json = JsonSerializer.Serialize(data);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _retryPolicy.ExecuteAsync(async () =>
            await _httpClient.PostAsync($"{endpoint}", content));

        if (response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(responseContent))
                return default;

            return JsonSerializer.Deserialize<T>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }

        var error = await response.Content.ReadAsStringAsync();
        _logger.LogError("POST request failed: {StatusCode} - {Error}", response.StatusCode, error);
        return default;
    }

    public async Task<bool> PatchAsync(string endpoint, object data)
    {
        await EnsureAuthenticatedAsync();

        var json = JsonSerializer.Serialize(data);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _retryPolicy.ExecuteAsync(async () =>
            await _httpClient.PatchAsync($"{endpoint}", content));

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("PATCH request failed: {StatusCode} - {Error}", response.StatusCode, error);
        }

        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Gets attachment metadata from the Attachments2 endpoint
    /// </summary>
    /// <param name="absoluteEntry">The absolute entry ID of the attachment</param>
    /// <returns>The attachment metadata or null if not found</returns>
    public async Task<Dictionary<string, object>?> GetAttachmentAsync(int absoluteEntry)
    {
        await EnsureAuthenticatedAsync();

        try
        {
            _logger.LogDebug("Fetching attachment metadata for AbsoluteEntry {AbsoluteEntry}", absoluteEntry);
            
            var endpoint = $"Attachments2({absoluteEntry})";
            var response = await _retryPolicy.ExecuteAsync(async () =>
                await _httpClient.GetAsync(endpoint));

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var attachment = JsonSerializer.Deserialize<Dictionary<string, object>>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                
                _logger.LogDebug("Successfully retrieved attachment metadata for AbsoluteEntry {AbsoluteEntry}", absoluteEntry);
                return attachment;
            }
            
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Attachment not found for AbsoluteEntry {AbsoluteEntry}", absoluteEntry);
                return null;
            }

            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Failed to get attachment: {StatusCode} - {Error}", response.StatusCode, error);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while fetching attachment metadata for AbsoluteEntry {AbsoluteEntry}", absoluteEntry);
            return null;
        }
    }

    /// <summary>
    /// Downloads an attachment file from SAP Service Layer
    /// </summary>
    /// <param name="attachmentPath">The attachment file path from the attachment metadata</param>
    /// <returns>The file content as a stream, or null if not found</returns>
    public async Task<Stream?> DownloadAttachmentFileAsync(string attachmentPath)
    {
        if (string.IsNullOrEmpty(attachmentPath))
        {
            _logger.LogWarning("Attachment path is null or empty");
            return null;
        }

        await EnsureAuthenticatedAsync();

        try
        {
            _logger.LogDebug("Downloading attachment file from path: {AttachmentPath}", attachmentPath);
            
            // The attachment path might be relative or absolute - handle both cases
            var endpoint = attachmentPath.StartsWith("http") 
                ? attachmentPath // Full URL
                : $"Attachments2/$value?filename={Uri.EscapeDataString(attachmentPath)}"; // Relative path
            
            var response = await _retryPolicy.ExecuteAsync(async () =>
                await _httpClient.GetAsync(endpoint));

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Successfully downloaded attachment from path: {AttachmentPath}", attachmentPath);
                return await response.Content.ReadAsStreamAsync();
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Attachment file not found at path: {AttachmentPath}", attachmentPath);
                return null;
            }

            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Failed to download attachment file: {StatusCode} - {Error}", response.StatusCode, error);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while downloading attachment file from path: {AttachmentPath}", attachmentPath);
            return null;
        }
    }

    /// <summary>
    /// Gets attachment metadata using SQL query (alternative method)
    /// </summary>
    /// <param name="absoluteEntry">The absolute entry ID of the attachment</param>
    /// <returns>The attachment details or null if not found</returns>
    public async Task<Dictionary<string, object>?> GetAttachmentDetailsViaSqlAsync(int absoluteEntry)
    {
        var query = $@"
            SELECT 
                T0.AbsEntry,
                T0.Line,
                T0.SrcObjTyp,
                T0.SrcObjAbs,
                T0.trgtPath,
                T0.FileName,
                T0.FileExt,
                T0.AttDate,
                T0.UsrID,
                T0.Descrip,
                T0.CopyToTrgt,
                T0.Override,
                T0.FileSize
            FROM ATT2 T0 
            WHERE T0.AbsEntry = {absoluteEntry}";

        var result = await ExecuteSqlQueryAsync(query);
        
        if (result != null && result.Count > 0)
        {
            return result[0];
        }
        
        return null;
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}