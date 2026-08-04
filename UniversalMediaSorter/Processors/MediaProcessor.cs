using UniversalMediaSorter.Location;
using UniversalMediaSorter.Metadata;
using UniversalMediaSorter.Services;
using UniversalMediaSorter.Utilities;

namespace UniversalMediaSorter.Processors
{
    /// <summary>
    /// Orchestrates the processing of media files: reads metadata, extracts location/date,
    /// reverse-geocodes, and copies files to a destination folder structure.
    /// </summary>
    public class MediaProcessor
    {
        private readonly IMetadataReader _metadataReader;
        private readonly ILocationExtractor _locationExtractor;
        private readonly IReverseGeocoder _reverseGeocoder;

        public MediaProcessor(
            IMetadataReader metadataReader,
            ILocationExtractor locationExtractor,
            IReverseGeocoder reverseGeocoder)
        {
            _metadataReader = metadataReader ?? throw new ArgumentNullException(nameof(metadataReader));
            _locationExtractor = locationExtractor ?? throw new ArgumentNullException(nameof(locationExtractor));
            _reverseGeocoder = reverseGeocoder ?? throw new ArgumentNullException(nameof(reverseGeocoder));
        }

        /// <summary>
        /// Processes a single media file: reads metadata, extracts date/location, geocodes, and copies to destination.
        /// </summary>
        /// <returns>Tuple of (success, destinationPath, errorMessage)</returns>
        public async Task<(bool Success, string? DestinationPath, string? Error)> ProcessFileAsync(string filePath, string outputRoot)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return (false, null, $"File not found: {filePath}");

            if (string.IsNullOrWhiteSpace(outputRoot))
                return (false, null, "Output root directory cannot be empty");

            try
            {
                // Step 1: Read metadata
                var metadataDirs = _metadataReader.Read(filePath);
                if (metadataDirs.Count == 0)
                    return (false, null, "Failed to read metadata");

                // Step 2: Extract date taken
                var (dateOk, dateTaken) = _locationExtractor.TryExtractDateTaken(metadataDirs, filePath);
                if (!dateOk)
                    return (false, null, "Failed to extract date taken");

                // Step 3: Extract location
                var (locOk, lat, lon) = _locationExtractor.TryExtractLocation(metadataDirs);

                var datePart = dateTaken.ToLocalTime().ToString("yyyyMMdd");
                string locationPart = "UnknownLocation";

                if (locOk)
                {
                    // Step 4: Reverse geocode
                    var place = await _reverseGeocoder.ReverseGeocodeAsync(lat, lon).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(place))
                        locationPart = FileUtilities.SanitizeFolderName(place);
                    else
                        locationPart = $"{lat:F6}_{lon:F6}";
                }

                // Step 5: Build destination path and copy file
                var destFolder = Path.Combine(outputRoot, $"{datePart} - {locationPart}");
                Directory.CreateDirectory(destFolder);

                var destFileName = Path.GetFileName(filePath);
                var destPath = Path.Combine(destFolder, destFileName);
                destPath = FileUtilities.GetSafeDestination(destPath);

                File.Copy(filePath, destPath, overwrite: false);

                return (true, destPath, null);
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }
    }
}
