namespace UniversalMediaSorter.Location
{
    /// <summary>
    /// Extracts location (GPS coordinates) and date taken from metadata dictionaries.
    /// Decoupled from specific metadata readers by accepting simple dictionaries.
    /// </summary>
    public interface ILocationExtractor
    {
        /// <summary>
        /// Try to extract GPS coordinates from metadata dictionaries.
        /// </summary>
        (bool Success, double Latitude, double Longitude) TryExtractLocation(IReadOnlyList<IDictionary<string, string>> metadataDirs);

        /// <summary>
        /// Try to extract the date/time the media was taken from metadata, with fallback to file creation time.
        /// </summary>
        (bool Success, DateTime DateTaken) TryExtractDateTaken(IReadOnlyList<IDictionary<string, string>> metadataDirs, string filePath);
    }
}
