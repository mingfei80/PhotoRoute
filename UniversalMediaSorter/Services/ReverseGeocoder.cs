using System.Net.Http.Headers;
using System.Text.Json;

namespace UniversalMediaSorter.Services
{
    /// <summary>
    /// Simple reverse geocoder using Nominatim (OpenStreetMap) with a file-backed cache.
    /// </summary>
    public class ReverseGeocoder : IReverseGeocoder, IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _cachePath;
        private readonly Dictionary<string, string> _cache = new();
        private readonly object _lock = new();

        public ReverseGeocoder(string? cachePath = null)
        {
            _http = new HttpClient();
            // Identify user-agent per Nominatim usage policy. Replace with a contact email if possible.
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("PhotoRoute/1.0 (contact: replace-with-your-email)");
            // Prefer English results where available
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
                        foreach (var kv in doc) _cache[kv.Key] = kv.Value;
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
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
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
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var v)) return v;
            }

            try
            {
                // Nominatim reverse geocoding API. Use a higher zoom for locality-level names.
                var url = $"https://nominatim.openstreetmap.org/reverse?format=jsonv2&lat={lat}&lon={lon}&zoom=14&addressdetails=1";
                using var resp = await _http.GetAsync(url).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;
                using var s = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(s).ConfigureAwait(false);
                if (doc.RootElement.TryGetProperty("address", out var addr))
                {
                    // Preferred locality selection per user rules:
                    // - If both (quarter|village|suburb|neighbourhood) and (city|town) present => "primary - secondary"
                    // - If only primary present => primary
                    // - If only secondary present => secondary
                    // - Else fallback to county/state/country/display_name
                    string? quarter = addr.TryGetProperty("quarter", out var q) ? q.GetString() : null;
                    string? neighbourhood = addr.TryGetProperty("neighbourhood", out var n) ? n.GetString() : null;
                    string? village = addr.TryGetProperty("village", out var v) ? v.GetString() : null;
                    string? suburb = addr.TryGetProperty("suburb", out var sub) ? sub.GetString() : null;

                    string? city = addr.TryGetProperty("city", out var ci) ? ci.GetString() : null;
                    string? town = addr.TryGetProperty("town", out var t) ? t.GetString() : null;

                    string? county = addr.TryGetProperty("county", out var c) ? c.GetString() : null;
                    string? state = addr.TryGetProperty("state", out var s2) ? s2.GetString() : null;
                    string? country = addr.TryGetProperty("country", out var c3) ? c3.GetString() : null;

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
                    else if (doc.RootElement.TryGetProperty("display_name", out var dn) && !string.IsNullOrEmpty(dn.GetString()))
                    {
                        var parts = dn.GetString()!.Split(',').Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p)).ToArray();
                        if (parts.Length >= 1) display = parts[0];
                    }

                    if (!string.IsNullOrWhiteSpace(display))
                    {
                        lock (_lock)
                        {
                            _cache[key] = display;
                            SaveCache();
                        }
                        return display;
                    }
                }
            }
            catch { }

            return null;
        }

        private void LogDebug(string text)
        {
            try
            {
                var p = Path.Combine(AppContext.BaseDirectory, "reversegeocode_debug.log");
                File.AppendAllText(p, DateTime.UtcNow.ToString("o") + " " + text + Environment.NewLine);
            }
            catch { }
        }

        public void Dispose()
        {
            SaveCache();
            _http.Dispose();
        }
    }
}
