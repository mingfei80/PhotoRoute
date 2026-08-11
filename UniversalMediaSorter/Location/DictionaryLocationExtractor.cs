using System.Globalization;
using System.Text.RegularExpressions;

namespace UniversalMediaSorter.Location
{
    /// <summary>
    /// Extracts GPS coordinates and date taken from metadata dictionaries.
    /// Supports various metadata formats including ISO 6709, DMS, and decimal degrees.
    /// </summary>
    public class DictionaryLocationExtractor : ILocationExtractor
    {
        private static readonly Regex Iso6709Regex = new Regex(
            @"^([+-]\d+\.\d+)([+-]\d+\.\d+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant
        );

        private static readonly string[] GpsKeys = new[]
        {
            "GPS Location",
            "location",
            "com.apple.quicktime.location.ISO6709",
            "GPSLatitude",
            "GPSLongitude"
        };

        private static readonly string[] DateKeys = new[]
        {
            // Photos (EXIF)
            "DateTimeOriginal",
            "Date/Time Original",
            // MOV (iPhone/Apple) – includes timezone offset, most accurate for video
            "com.apple.quicktime.creationdate",
            "CreationDate",
            "Creation Date",
            "CreateDate",
            "Create Date",
            // MP4 / QuickTime Movie Header – stored as UTC
            "Created",
            "ModifyDate",
            "Modify Date",
            "DateTime",
            "Date Time"
        };

        public (bool Success, double Latitude, double Longitude) TryExtractLocation(IReadOnlyList<IDictionary<string, string>> metadataDirs)
        {
            if (metadataDirs == null || metadataDirs.Count == 0)
                return (false, 0, 0);

            // Scan all directories for GPS-related keys
            foreach (var dir in metadataDirs)
            {
                // First priority: raw GPS coordinates extracted by MetadataExtractorReader
                if (dir.TryGetValue("__RawGPSLatitude", out var rawLat) && 
                    dir.TryGetValue("__RawGPSLongitude", out var rawLon))
                {
                    if (double.TryParse(rawLat, out var lat) && double.TryParse(rawLon, out var lon))
                    {
                        return (true, lat, lon);
                    }
                }

                // Second priority: GPS Location tag (usually ISO 6709 format or comma-separated)
                foreach (var key in GpsKeys)
                {
                    if (dir.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                    {
                        // Try ISO 6709 format first (e.g., "+50.7877-004.4669+100.811/")
                        if (TryParseIso6709(value, out var lat, out var lon))
                        {
                            return (true, lat, lon);
                        }

                        // Try comma-separated format (e.g., "50.7877, -4.4669")
                        if (TryParseCommaSeparated(value, out lat, out lon))
                        {
                            return (true, lat, lon);
                        }
                    }
                }

                // Third priority: explicit latitude/longitude pairs
                if (dir.TryGetValue("GPSLatitude", out var latStr) && 
                    dir.TryGetValue("GPSLongitude", out var lonStr))
                {
                    if (TryParseCoordinate(latStr, out var lat) && 
                        TryParseCoordinate(lonStr, out var lon))
                    {
                        // Check for hemisphere indicators
                        if (dir.TryGetValue("GPSLatitudeRef", out var latRef) && latRef == "S")
                            lat = -lat;
                        if (dir.TryGetValue("GPSLongitudeRef", out var lonRef) && lonRef == "W")
                            lon = -lon;

                        return (true, lat, lon);
                    }
                }

                // Fourth priority: simple "Latitude" and "Longitude" keys
                if (dir.TryGetValue("Latitude", out var latStr2) && 
                    dir.TryGetValue("Longitude", out var lonStr2))
                {
                    if (TryParseCoordinate(latStr2, out var lat) && 
                        TryParseCoordinate(lonStr2, out var lon))
                    {
                        return (true, lat, lon);
                    }
                }
            }

            return (false, 0, 0);
        }

        public (bool Success, DateTime DateTaken) TryExtractDateTaken(IReadOnlyList<IDictionary<string, string>> metadataDirs, string filePath)
        {
            if (metadataDirs != null && metadataDirs.Count > 0)
            {
                // Scan all directories for date-related keys
                foreach (var dir in metadataDirs)
                {
                    foreach (var key in DateKeys)
                    {
                        if (dir.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                        {
                            if (TryParseDateTime(value, out var dateTaken))
                            {
                                return (true, dateTaken);
                            }
                        }
                    }
                }
            }

            // Fallback to file creation time (UTC)
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                var creationTime = File.GetCreationTimeUtc(filePath);
                return (true, creationTime);
            }

            return (false, DateTime.MinValue);
        }

        private static bool TryParseCommaSeparated(string s, out double lat, out double lon)
        {
            lat = 0;
            lon = 0;

            if (string.IsNullOrWhiteSpace(s))
                return false;

            // Match comma-separated format: "50.7877, -4.4669" or similar
            var parts = s.Split(',');
            if (parts.Length != 2)
                return false;

            return double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out lat) &&
                   double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out lon);
        }

        private static bool TryParseIso6709(string s, out double lat, out double lon)
        {
            lat = 0;
            lon = 0;

            if (string.IsNullOrWhiteSpace(s))
                return false;

            var match = Iso6709Regex.Match(s);
            if (!match.Success)
                return false;

            return double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out lat) &&
                   double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out lon);
        }

        private static bool TryParseCoordinate(string s, out double result)
        {
            result = 0;

            if (string.IsNullOrWhiteSpace(s))
                return false;

            // Try decimal first
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
                return true;

            // Try DMS format (e.g., "51° 30' 15.5\"" or "51 30 15.5")
            return TryParseDms(s, out result);
        }

        private static bool TryParseDms(string dms, out double result)
        {
            result = 0;

            if (string.IsNullOrWhiteSpace(dms))
                return false;

            // Match patterns like: "51° 30' 15.5\"" or "51 30 15.5"
            var match = Regex.Match(dms, @"(\d+(?:\.\d+)?)[°\s]+(\d+(?:\.\d+)?)['\s]+(\d+(?:\.\d+)?)");
            if (!match.Success)
                return false;

            if (!double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var degrees))
                return false;
            if (!double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes))
                return false;
            if (!double.TryParse(match.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                return false;

            result = degrees + (minutes / 60.0) + (seconds / 3600.0);
            return true;
        }

        private static bool TryParseDateTime(string s, out DateTime result)
        {
            result = DateTime.MinValue;

            if (string.IsNullOrWhiteSpace(s))
                return false;

            // Strip any trailing timezone name in brackets, e.g. "2026-05-24T16:00:00+08:00 [Asia/Singapore]"
            var trimmed = Regex.Replace(s.Trim(), @"\s*\[.*?\]\s*$", string.Empty);

            // ISO 8601 with timezone offset (com.apple.quicktime.creationdate: "2026-05-24T16:00:00+08:00")
            // Parse as DateTimeOffset so the offset is respected, then convert to UTC for consistent handling.
            if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
            {
                result = dto.UtcDateTime;
                return true;
            }

            // Common EXIF format: "2026:06:29 21:03:12" (local, no offset)
            if (DateTime.TryParseExact(trimmed, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
                return true;

            // QuickTime/MP4 "Created" UTC format: "Sat Nov 15 06:04:32 2025"
            if (DateTime.TryParseExact(trimmed, "ddd MMM dd HH:mm:ss yyyy",
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result))
                return true;

            // QuickTime/MP4 single-digit day variant: "Sat Nov  5 06:04:32 2025"
            if (DateTime.TryParseExact(trimmed, "ddd MMM  d HH:mm:ss yyyy",
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result))
                return true;

            return false;
        }
    }
}
