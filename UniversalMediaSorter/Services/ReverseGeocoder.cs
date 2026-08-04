using System.Net.Http.Headers;
using System.Text.Json;

namespace UniversalMediaSorter.Services
{
    /// <summary>
    /// Exception raised when Nominatim returns a 429 (Too Many Requests) status.
    /// </summary>
    public class RateLimitedException : HttpRequestException
    {
        public RateLimitedException(string message) : base(message) { }
    }

    /// <summary>
    /// Exception raised when Nominatim returns a 503 (Service Unavailable) status.
    /// </summary>
    public class ServiceUnavailableException : HttpRequestException
    {
        public ServiceUnavailableException(string message) : base(message) { }
    }

    /// <summary>
    /// Reverse geocoder using Nominatim (OpenStreetMap) with file-backed cache,
    /// rate limiting, and retry logic per Nominatim's usage policy.
    /// </summary>
    public class ReverseGeocoder : IReverseGeocoder, IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _cachePath;
        private readonly Dictionary<string, string> _cache = new();
        private readonly SemaphoreSlim _rateLimitSemaphore = new(1, 1); // Serialize requests
        private DateTime _lastRequestTime = DateTime.UtcNow; // Initialize properly
        private const int MinDelayBetweenRequests = 1100; // 1.1 seconds, safer than 1s

        public ReverseGeocoder(string? cachePath = null)
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("PhotoRoute/1.0 (contact: replace-with-your-email)");
            _http.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en"));

            _cachePath = cachePath ?? Path.Combine(AppContext.BaseDirectory, "reversegeocode_cache.json");
            LoadCache();
        }

        private void LoadCache()
        {
            try
            {
                if (File.Exists(_cachePath))
                {
                    var json = File.ReadAllText(_cachePath);
                    var doc = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (doc != null)
                    {
                        foreach (var kv in doc)
                            _cache[kv.Key] = kv.Value;
                    }
                }
            }
            catch { }
        }

        private void SaveCache()
        {
            try
            {
                var dir = Path.GetDirectoryName(_cachePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(_cache);
                File.WriteAllText(_cachePath, json);
            }
            catch { }
        }

        private static string KeyFor(double lat, double lon, int precision = 5)
        {
            return $"{Math.Round(lat, precision)}_{Math.Round(lon, precision)}";
        }

        public async Task<string?> ReverseGeocodeAsync(double lat, double lon)
        {
            var key = KeyFor(lat, lon);

            // Check cache first (no lock needed, dictionary is thread-safe for reads in .NET)
            if (_cache.TryGetValue(key, out var cached))
            {
                LogDebug($"Cache hit for {lat},{lon} -> {cached}");
                return cached;
            }

            // Acquire semaphore to serialize requests and enforce rate limiting
            await _rateLimitSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                // Check cache again in case another thread just completed this request
                if (_cache.TryGetValue(key, out var cached2))
                {
                    LogDebug($"Cache hit after acquire for {lat},{lon} -> {cached2}");
                    return cached2;
                }

                // Enforce rate limiting
                var elapsed = (DateTime.UtcNow - _lastRequestTime).TotalMilliseconds;
                if (elapsed < MinDelayBetweenRequests)
                {
                    var delay = (int)(MinDelayBetweenRequests - elapsed);
                    LogDebug($"Rate limit: waiting {delay}ms before request");
                    await Task.Delay(delay).ConfigureAwait(false);
                }

                // Try with exponential backoff on rate limit errors
                int maxRetries = 5;
                for (int retryCount = 0; retryCount < maxRetries; retryCount++)
                {
                    try
                    {
                        _lastRequestTime = DateTime.UtcNow; // Record time of EACH attempt
                        var result = await ReverseGeocodeInternalAsync(lat, lon).ConfigureAwait(false);
                        if (result != null)
                        {
                            // Cache the result
                            _cache[key] = result;
                            SaveCache();
                            LogDebug($"Geocoded {lat},{lon} -> {result}");
                            return result;
                        }

                        // API returned null (no result found)
                        LogDebug($"No geocoding result for {lat},{lon}");
                        return null;
                    }
                    catch (RateLimitedException)
                    {
                        // Rate limited by Nominatim - exponential backoff retry
                        if (retryCount < maxRetries - 1)
                        {
                            int delayMs = (int)(Math.Pow(2, retryCount) * 1100); // 1.1s, 2.2s, 4.4s, etc
                            LogDebug($"429 Rate limited, waiting {delayMs}ms before retry {retryCount + 1}/{maxRetries}");
                            await Task.Delay(delayMs).ConfigureAwait(false);
                            // Continue loop to retry
                        }
                        else
                        {
                            LogDebug($"429 Rate limited - no more retries, giving up");
                            return null;
                        }
                    }
                    catch (ServiceUnavailableException)
                    {
                        // Service unavailable - similar backoff
                        if (retryCount < maxRetries - 1)
                        {
                            int delayMs = (int)(Math.Pow(2, retryCount) * 1100);
                            LogDebug($"503 Service unavailable, waiting {delayMs}ms before retry {retryCount + 1}/{maxRetries}");
                            await Task.Delay(delayMs).ConfigureAwait(false);
                            // Continue loop to retry
                        }
                        else
                        {
                            LogDebug($"503 Service unavailable - no more retries, giving up");
                            return null;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogDebug($"Geocoding error for {lat},{lon}: {ex.Message}");
                        // Don't retry on other exceptions
                        return null;
                    }
                }

                return null;
            }
            finally
            {
                _rateLimitSemaphore.Release();
            }
        }

        private async Task<string?> ReverseGeocodeInternalAsync(double lat, double lon)
        {
            try
            {
                var url = $"https://nominatim.openstreetmap.org/reverse?format=jsonv2&lat={lat}&lon={lon}&zoom=14&addressdetails=1";

                using var resp = await _http.GetAsync(url).ConfigureAwait(false);

                // Check for rate limiting and service errors
                if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    throw new RateLimitedException("Rate limited");
                }

                if (resp.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                {
                    throw new ServiceUnavailableException("Service unavailable");
                }

                if (!resp.IsSuccessStatusCode)
                {
                    LogDebug($"Nominatim returned {resp.StatusCode} for {lat},{lon}");
                    return null;
                }

                using var s = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(s).ConfigureAwait(false);

                if (doc.RootElement.TryGetProperty("address", out var addr))
                {
                    // Extract address components with locality preference
                    string? quarter = TryGetString(addr, "quarter");
                    string? neighbourhood = TryGetString(addr, "neighbourhood");
                    string? village = TryGetString(addr, "village");
                    string? suburb = TryGetString(addr, "suburb");
                    string? city = TryGetString(addr, "city");
                    string? town = TryGetString(addr, "town");
                    string? county = TryGetString(addr, "county");
                    string? state = TryGetString(addr, "state");
                    string? country = TryGetString(addr, "country");

                    string? primary = quarter ?? village ?? suburb ?? neighbourhood;
                    string? secondary = city ?? town;

                    string? display = null;
                    if (!string.IsNullOrWhiteSpace(primary) && !string.IsNullOrWhiteSpace(secondary))
                        display = $"{primary} - {secondary}";
                    else if (!string.IsNullOrWhiteSpace(primary))
                        display = primary;
                    else if (!string.IsNullOrWhiteSpace(secondary))
                        display = secondary;
                    else if (!string.IsNullOrWhiteSpace(county))
                        display = county;
                    else if (!string.IsNullOrWhiteSpace(state))
                        display = state;
                    else if (!string.IsNullOrWhiteSpace(country))
                        display = country;
                    else if (doc.RootElement.TryGetProperty("display_name", out var dn))
                    {
                        var displayName = dn.GetString();
                        if (!string.IsNullOrEmpty(displayName))
                        {
                            var parts = displayName.Split(',').Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p)).ToArray();
                            if (parts.Length >= 1)
                                display = parts[0];
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(display))
                        return display;
                }
            }
            catch (RateLimitedException ex)
            {
                // Re-throw rate limit exceptions for retry logic
                throw;
            }
            catch (ServiceUnavailableException ex)
            {
                // Re-throw service unavailable exceptions for retry logic
                throw;
            }
            catch (Exception ex)
            {
                LogDebug($"Exception in ReverseGeocodeInternalAsync: {ex.GetType().Name}: {ex.Message}");
            }

            return null;
        }

        private static string? TryGetString(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var prop))
                return prop.GetString();
            return null;
        }

        private void LogDebug(string text)
        {
            try
            {
                var p = Path.Combine(AppContext.BaseDirectory, "reversegeocode_debug.log");
                File.AppendAllText(p, $"{DateTime.UtcNow:o} {text}{Environment.NewLine}");
            }
            catch { }
        }

        public void Dispose()
        {
            SaveCache();
            _http.Dispose();
            _rateLimitSemaphore.Dispose();
        }
    }
}
