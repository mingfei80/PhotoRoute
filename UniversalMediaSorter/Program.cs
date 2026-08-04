using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using MetadataExtractor;
using Microsoft.Extensions.Configuration;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.QuickTime;

namespace UniversalMediaSorter
{
    // Simple utility app: read config.json (Source/Output), scan source, extract DateTaken and Location via MetadataExtractor,
    // and copy files into folders named {YYYYMMDD}-{Location} where Location is lat_lon or UnknownLocation.
    internal class Program
    {
        static async System.Threading.Tasks.Task<int> Main(string[] args)
        {
            try
            {
                // Use Microsoft.Extensions.Configuration to load config.json from multiple locations.
                var builder = new ConfigurationBuilder()
                    .SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("config.json", optional: true, reloadOnChange: false)
                    .AddJsonFile(Path.Combine(Environment.CurrentDirectory, "config.json"), optional: true, reloadOnChange: false);

                var configuration = builder.Build();

                string? source = configuration["Source"];
                string? output = configuration["Output"];

                // Fallback to command-line args when not set in configuration
                if (string.IsNullOrWhiteSpace(source)) source = args.Length > 0 ? args[0] : null;
                if (string.IsNullOrWhiteSpace(output)) output = args.Length > 1 ? args[1] : null;

                if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("Source not provided");
                if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("Output not provided");

                if (!System.IO.Directory.Exists(source))
                {
                    Console.Error.WriteLine($"Source directory not found: {source}");
                    return 2;
                }

                System.IO.Directory.CreateDirectory(output);

                var extensions = new[] { ".jpg", ".jpeg", ".heic", ".heif", ".png", ".mp4", ".mov", ".m4v", ".3gp" };

                var files = System.IO.Directory.EnumerateFiles(source, "*.*", System.IO.SearchOption.AllDirectories)
                    .Where(f => extensions.Contains(Path.GetExtension(f)?.ToLowerInvariant()));

                using var geocoder = new Services.ReverseGeocoder();

                foreach (var file in files)
                {
                    try
                    {
                        var dirs = ReadMetadataByExtension(file);

                        var (dateOk, dateTaken) = GetDateTaken(dirs, file);
                        var (locOk, lat, lon) = GetGps(dirs);

                        var datePart = dateTaken.ToLocalTime().ToString("yyyyMMdd");
                        string locationPart;
                        if (locOk)
                        {
                            // Attempt reverse geocoding (online). Cache helps limit requests.
                            var place = await geocoder.ReverseGeocodeAsync(lat, lon).ConfigureAwait(false);
                            if (!string.IsNullOrWhiteSpace(place))
                                locationPart = SanitizeFolderName(place);
                            else
                                locationPart = $"{lat:F6}_{lon:F6}";
                        }
                        else
                        {
                            locationPart = "UnknownLocation";
                        }

                        var destFolder = Path.Combine(output, $"{datePart} - {locationPart}");
                        System.IO.Directory.CreateDirectory(destFolder);

                        var destPath = Path.Combine(destFolder, Path.GetFileName(file));
                        destPath = GetSafeDestination(destPath);

                        System.IO.File.Copy(file, destPath);
                        Console.WriteLine($"Copied: {file} -> {destPath}");
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Failed: {file} -> {ex.Message}");
                    }
                }

                Console.WriteLine("Done.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        private static System.Collections.Generic.IEnumerable<MetadataExtractor.Directory> ReadMetadataByExtension(string path)
        {
            var ext = Path.GetExtension(path)?.ToLowerInvariant();
            try
            {
                if (ext is ".mov")
                {
                    using var fs = System.IO.File.OpenRead(path);
                    return QuickTimeMetadataReader.ReadMetadata(fs);
                }
                // Fallback: ImageMetadataReader handles many image types; MP4 support may be limited depending on package.
                return ImageMetadataReader.ReadMetadata(path);
            }
            catch
            {
                return System.Array.Empty<MetadataExtractor.Directory>();
            }
        }

        private static (bool, DateTime) GetDateTaken(System.Collections.Generic.IEnumerable<MetadataExtractor.Directory> dirs, string path)
        {
            try
            {
                var exif = dirs.OfType<ExifSubIfdDirectory>().FirstOrDefault();
                if (exif != null && exif.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out DateTime dt))
                    return (true, dt);
            }
            catch { }

            try { return (true, System.IO.File.GetCreationTimeUtc(path)); } catch { return (false, DateTime.UtcNow); }
        }

        private static (bool, double, double) GetGps(System.Collections.Generic.IEnumerable<MetadataExtractor.Directory> dirs)
        {
            try
            {
                var gps = dirs.OfType<GpsDirectory>().FirstOrDefault();
                if (gps != null)
                {
                    var geo = gps.GetGeoLocation();
                    // GeoLocation is a nullable struct in some versions; handle HasValue
                    if (geo.HasValue)
                    {
                        var g = geo.Value;
                        if (!double.IsNaN(g.Latitude) && !double.IsNaN(g.Longitude))
                            return (true, g.Latitude, g.Longitude);
                    }
                }
            }
            catch { }
            return (false, 0, 0);
        }

        private static string GetSafeDestination(string path)
        {
            if (!File.Exists(path)) return path;
            var dir = Path.GetDirectoryName(path)!;
            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            int i = 1;
            string candidate;
            do
            {
                candidate = Path.Combine(dir, $"{name} ({i}){ext}");
                i++;
            } while (File.Exists(candidate));
            return candidate;
        }

        private static string SanitizeFolderName(string name)
        {
            // Remove invalid path chars and collapse whitespace
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
            return string.Join(' ', cleaned.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
        }
    }
}
