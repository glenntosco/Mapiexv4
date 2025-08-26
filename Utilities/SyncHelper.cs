using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace P4WIntegration.Utilities;

public static class SyncHelper
{
    public static string CalculateHash(object data)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = false });
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(json));
        return Convert.ToBase64String(hashBytes);
    }

    public static T GetValueOrDefault<T>(this Dictionary<string, object> dictionary, string key, T defaultValue)
    {
        if (dictionary.TryGetValue(key, out var value))
        {
            if (value is T typedValue)
                return typedValue;
            
            try
            {
                if (value != null)
                {
                    // Handle type conversions
                    if (typeof(T) == typeof(decimal))
                    {
                        return (T)(object)Convert.ToDecimal(value);
                    }
                    if (typeof(T) == typeof(int))
                    {
                        return (T)(object)Convert.ToInt32(value);
                    }
                    if (typeof(T) == typeof(bool))
                    {
                        if (value is string strValue)
                        {
                            return (T)(object)(strValue == "Y" || strValue == "1" || strValue.Equals("true", StringComparison.OrdinalIgnoreCase));
                        }
                        return (T)(object)Convert.ToBoolean(value);
                    }
                    if (typeof(T) == typeof(DateTime))
                    {
                        return (T)(object)Convert.ToDateTime(value);
                    }
                    if (typeof(T) == typeof(string))
                    {
                        return (T)(object)value.ToString()!;
                    }
                }
            }
            catch
            {
                // If conversion fails, return default
            }
        }
        return defaultValue;
    }

    public static DateTime ParseSapDate(string? dateString)
    {
        if (string.IsNullOrEmpty(dateString))
            return DateTime.MinValue;

        if (DateTime.TryParse(dateString, out var date))
            return date;

        // Handle SAP's specific date formats
        if (dateString.Length == 8) // YYYYMMDD
        {
            if (int.TryParse(dateString.Substring(0, 4), out var year) &&
                int.TryParse(dateString.Substring(4, 2), out var month) &&
                int.TryParse(dateString.Substring(6, 2), out var day))
            {
                try
                {
                    return new DateTime(year, month, day);
                }
                catch { }
            }
        }

        return DateTime.MinValue;
    }

    public static string FormatSapDate(DateTime date)
    {
        return date.ToString("yyyy-MM-dd HH:mm:ss");
    }

    public static decimal SafeDecimalParse(object? value, decimal defaultValue = 0m)
    {
        if (value == null)
            return defaultValue;

        if (value is decimal d)
            return d;

        if (decimal.TryParse(value.ToString(), out var result))
            return result;

        return defaultValue;
    }

    public static int SafeIntParse(object? value, int defaultValue = 0)
    {
        if (value == null)
            return defaultValue;

        if (value is int i)
            return i;

        if (int.TryParse(value.ToString(), out var result))
            return result;

        return defaultValue;
    }
}