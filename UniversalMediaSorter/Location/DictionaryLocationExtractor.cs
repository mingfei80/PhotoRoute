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
            "DateTimeOriginal",
            "Date/Time Original",
            "CreationDate",
            "Creation Date",
            "CreateDate",
            "Create Date",
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
                foreach (var key in GpsKeys)
                {
                    if (dir.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                    {
                        // Try ISO 6709 format first (e.g., "+50.7877-004.4669+100.811/")
                        if (TryParseIso6709(value, out var lat, out var lon))
                        {
                            return (true, lat, lon);
                        }
                    }
                }

                // Try explicit latitude/longitude pairs
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

                // Try simple "Latitude" and "Longitude" keys
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

            // Try standard DateTime parsing
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
                return true;

            // Try common EXIF format: "2026:06:29 21:03:12"
            if (DateTime.TryParseExact(s, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
                return true;

            return false;
        }
    }
}
