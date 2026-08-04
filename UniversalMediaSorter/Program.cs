using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.QuickTime;
using Microsoft.Extensions.Configuration;

namespace UniversalMediaSorter
{
    internal static class Program
    {
        // Entry point
        public static async Task<int> Main(string[] args)
        {
            try
            {
                var builder = new ConfigurationBuilder()
                    .SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("config.json", optional: true, reloadOnChange: false)
                    .AddJsonFile(Path.Combine(Environment.CurrentDirectory, "config.json"), optional: true, reloadOnChange: false);

                var configuration = builder.Build();
                string? source = configuration["Source"]; 
                string? output = configuration["Output"];

                // dump mode
                if (args.Length > 0 && args[0] == "--dump")
                {
                    if (args.Length < 2) { Console.Error.WriteLine("Usage: --dump <file>"); return 1; }
                    var target = args[1];
                    if (!File.Exists(target)) { Console.Error.WriteLine($"File not found: {target}"); return 2; }
                    DumpMetadata(target);
                    return 0;
                }

                if (string.IsNullOrWhiteSpace(source)) source = args.Length > 0 ? args[0] : null;
                if (string.IsNullOrWhiteSpace(output)) output = args.Length > 1 ? args[1] : null;

                if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(output))
                {
                    Console.Error.WriteLine("Source and Output must be provided via config.json or command-line args.");
                    return 2;
                }

                if (!System.IO.Directory.Exists(source)) { Console.Error.WriteLine($"Source not found: {source}"); return 3; }
                System.IO.Directory.CreateDirectory(output);

                var exts = new[] { ".jpg", ".jpeg", ".heic", ".heif", ".png", ".mp4", ".mov", ".m4v", ".3gp" };
                var files = System.IO.Directory.EnumerateFiles(source, "*.*", System.IO.SearchOption.AllDirectories)
                    .Where(f => exts.Contains(Path.GetExtension(f)?.ToLowerInvariant()));

                using var geocoder = new Services.ReverseGeocoder();

                foreach (var file in files)
                {
                    try
                    {
                        var dirs = ReadMetadataByExtension(file);
                        var (dateOk, dateTaken) = GetDateTaken(dirs, file);
                        var (locOk, lat, lon) = GetGps(dirs);

                        var datePart = dateTaken.ToLocalTime().ToString("yyyyMMdd");
                        string locationPart = "UnknownLocation";
                        if (locOk)
                        {
                            var place = await geocoder.ReverseGeocodeAsync(lat, lon).ConfigureAwait(false);
                            if (!string.IsNullOrWhiteSpace(place)) locationPart = SanitizeFolderName(place);
                            else locationPart = $"{lat:F6}_{lon:F6}";
                        }

                        var destFolder = Path.Combine(output, $"{datePart} - {locationPart}");
                        System.IO.Directory.CreateDirectory(destFolder);
                        var destPath = Path.Combine(destFolder, Path.GetFileName(file));
                        destPath = GetSafeDestination(destPath);
                        File.Copy(file, destPath);
                        Console.WriteLine($"Copied: {file} -> {destPath}");
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Failed {file}: {ex.Message}");
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        private static IEnumerable<MetadataExtractor.Directory> ReadMetadataByExtension(string path)
        {
            var ext = Path.GetExtension(path)?.ToLowerInvariant();
            try
            {
                if (ext == ".mov")
                {
                    using var fs = File.OpenRead(path);
                    return QuickTimeMetadataReader.ReadMetadata(fs);
                }
                // Try Mp4 via ImageMetadataReader fallback for many formats
                return ImageMetadataReader.ReadMetadata(path);
            }
            catch
            {
                return Array.Empty<MetadataExtractor.Directory>();
            }
        }



        private static (bool, double, double) GetGps(IEnumerable<MetadataExtractor.Directory> dirs)
        {
            try
            {
                // reflection-based attempt to find GPS directory
                foreach (var d in dirs)
                {
                    var typeName = d.GetType().Name ?? string.Empty;
                    if (typeName.IndexOf("GpsDirectory", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var mi = d.GetType().GetMethod("GetGeoLocation");
                        if (mi != null)
                        {
                            var geoObj = mi.Invoke(d, null);
                            if (geoObj != null)
                            {
                                var latProp = geoObj.GetType().GetProperty("Latitude");
                                var lonProp = geoObj.GetType().GetProperty("Longitude");
                                if (latProp != null && lonProp != null)
                                {
                                    var latVal = latProp.GetValue(geoObj);
                                    var lonVal = lonProp.GetValue(geoObj);
                                    if (latVal is double lat && lonVal is double lon)
                                    {
                                        if (!double.IsNaN(lat) && !double.IsNaN(lon)) return (true, lat, lon);
                                    }
                                }
                            }
                        }
                    }
                }

                // parse textual tag fallbacks
                string latDesc = null, lonDesc = null;
                foreach (var d in dirs)
                {
                    foreach (var tag in d.Tags)
                    {
                        var name = (tag.Name ?? string.Empty).ToLowerInvariant();
                        if (name.Contains("gps latitude") || name.Contains("gpslat") || name.Contains("latitude")) latDesc ??= tag.Description;
                        if (name.Contains("gps longitude") || name.Contains("gpslon") || name.Contains("longitude")) lonDesc ??= tag.Description;
                        if (latDesc != null && lonDesc != null) break;
                    }
                    if (latDesc != null && lonDesc != null) break;
                }

                // ISO6709 style in some QuickTime tags: +51.515114-0.082844/
                if (latDesc == null || lonDesc == null)
                {
                    foreach (var d in dirs)
                    {
                        foreach (var tag in d.Tags)
                        {
                            if (tag.Description != null && tag.Description.Contains("+") && tag.Description.Contains("/"))
                            {
                                var maybe = tag.Description.Trim();
                                var iso = ParseIso6709(maybe);
                                if (iso != null) return (true, iso.Value.lat, iso.Value.lon);
                            }
                        }
                    }
                }

                if (!string.IsNullOrEmpty(latDesc) && !string.IsNullOrEmpty(lonDesc))
                {
                    if (TryParseDms(latDesc, out double lat) && TryParseDms(lonDesc, out double lon)) return (true, lat, lon);
                }
            }
            catch { }
            return (false, 0, 0);
        }

        private static (double lat, double lon)? ParseIso6709(string s)
        {
            // Improved ISO6709 parser using regex to extract two signed decimal numbers (lat, lon)
            try
            {
                if (string.IsNullOrWhiteSpace(s)) return null;
                var trimmed = s.Trim();
                // Remove any trailing slash
                trimmed = trimmed.TrimEnd('/');
                var rx = new System.Text.RegularExpressions.Regex(@"(?<lat>[+-]\d+(?:\.\d+)?)[^0-9+-]*?(?<lon>[+-]\d+(?:\.\d+)?)");
                var m = rx.Match(trimmed);
                if (!m.Success) return null;
                var latStr = m.Groups["lat"].Value;
                var lonStr = m.Groups["lon"].Value;
                if (double.TryParse(latStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lat)
                    && double.TryParse(lonStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lon))
                {
                    return (lat, lon);
                }
            }
            catch { }
            return null;
        }

        private static bool TryParseDms(string dms, out double result)
        {
            result = double.NaN;
            if (string.IsNullOrWhiteSpace(dms)) return false;
            var cleaned = dms.Trim();
            if (double.TryParse(cleaned, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dec)) { result = dec; return true; }
            try
            {
                // remove degree symbols
                var parts = cleaned.Replace("°", " ").Replace("deg", " ").Replace("\"", " ").Replace("'", " ")
                    .Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                var nums = new List<double>();
                foreach (var p in parts)
                {
                    if (double.TryParse(p, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) nums.Add(v);
                    else break;
                }
                if (nums.Count == 0) return false;
                double degrees = nums[0];
                double minutes = nums.Count > 1 ? nums[1] : 0;
                double seconds = nums.Count > 2 ? nums[2] : 0;
                var value = degrees + minutes / 60.0 + seconds / 3600.0;
                var last = parts.Last();
                if (last.EndsWith("S", StringComparison.OrdinalIgnoreCase) || last.EndsWith("W", StringComparison.OrdinalIgnoreCase)) value = -Math.Abs(value);
                result = value; return true;
            }
            catch { return false; }
        }

        private static (bool, DateTime) GetDateTimeFromDirs(IEnumerable<MetadataExtractor.Directory> dirs)
        {
            try
            {
                var exif = dirs.OfType<ExifSubIfdDirectory>().FirstOrDefault();
                if (exif != null && exif.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out DateTime dt)) return (true, dt);
            }
            catch { }
            return (false, DateTime.MinValue);
        }

        private static (bool, DateTime) GetDateTaken(IEnumerable<MetadataExtractor.Directory> dirs, string path)
        {
            var dt = GetDateTimeFromDirs(dirs);
            if (dt.Item1) return dt;
            try { return (true, File.GetCreationTimeUtc(path)); } catch { return (false, DateTime.UtcNow); }
        }

        private static void DumpMetadata(string path)
        {
            Console.WriteLine($"Dumping metadata for: {path}");
            var dirs = ReadMetadataByExtension(path).ToArray();
            if (dirs.Length == 0) { Console.WriteLine("No metadata directories found."); return; }
            foreach (var d in dirs)
            {
                Console.WriteLine($"--- Directory: {d.Name} ({d.GetType().Name}) ---");
                foreach (var tag in d.Tags)
                {
                    Console.WriteLine($"{tag.Name} = {tag.Description}");
                }
            }
        }

        private static string GetSafeDestination(string path)
        {
            if (!File.Exists(path)) return path;
            var dir = Path.GetDirectoryName(path)!;
            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            int i = 1; string candidate;
            do { candidate = Path.Combine(dir, $"{name} ({i}){ext}"); i++; } while (File.Exists(candidate));
            return candidate;
        }

        private static string SanitizeFolderName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
            return string.Join(' ', cleaned.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
        }
    }
}
