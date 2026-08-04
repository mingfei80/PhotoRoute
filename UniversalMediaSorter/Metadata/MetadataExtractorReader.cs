using MetadataExtractor;
using MetadataExtractor.Formats.QuickTime;

namespace UniversalMediaSorter.Metadata
{
    /// <summary>
    /// Uses MetadataExtractor to read metadata and converts directories to dictionaries.
    /// Extracts raw GPS values from GpsDirectory when available.
    /// </summary>
    public class MetadataExtractorReader : IMetadataReader
    {
        public IReadOnlyList<IDictionary<string, string>> Read(string filePath)
        {
            var ext = Path.GetExtension(filePath)?.ToLowerInvariant();
            try
            {
                IEnumerable<MetadataExtractor.Directory> dirs;
                if (ext == ".mov")
                {
                    using var fs = File.OpenRead(filePath);
                    dirs = QuickTimeMetadataReader.ReadMetadata(fs);
                }
                else
                {
                    dirs = ImageMetadataReader.ReadMetadata(filePath);
                }

                var result = new List<IDictionary<string, string>>();
                foreach (var d in dirs)
                {
                    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    // Add all standard tags
                    foreach (var tag in d.Tags)
                    {
                        try { map[tag.Name] = tag.Description ?? string.Empty; } catch { }
                    }

                    // Try to extract raw GPS coordinates from GpsDirectory
                    if (d.GetType().Name.Contains("GpsDirectory"))
                    {
                        try
                        {
                            var getGeoLocationMethod = d.GetType().GetMethod("GetGeoLocation");
                            if (getGeoLocationMethod != null)
                            {
                                var geoLocation = getGeoLocationMethod.Invoke(d, null);
                                if (geoLocation != null)
                                {
                                    var latProp = geoLocation.GetType().GetProperty("Latitude");
                                    var lonProp = geoLocation.GetType().GetProperty("Longitude");

                                    if (latProp != null && lonProp != null)
                                    {
                                        var latVal = latProp.GetValue(geoLocation);
                                        var lonVal = lonProp.GetValue(geoLocation);

                                        if (latVal is double lat && lonVal is double lon && !double.IsNaN(lat) && !double.IsNaN(lon))
                                        {
                                            // Store raw coordinates with special keys
                                            map["__RawGPSLatitude"] = lat.ToString("F8");
                                            map["__RawGPSLongitude"] = lon.ToString("F8");
                                        }
                                    }
                                }
                            }
                        }
                        catch { }
                    }

                    result.Add(map);
                }
                return result;
            }
            catch
            {
                return Array.Empty<IDictionary<string, string>>();
            }
        }
    }
}
