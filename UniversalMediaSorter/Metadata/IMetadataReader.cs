namespace UniversalMediaSorter.Metadata
{
    /// <summary>
    /// Reads metadata for a file and returns a collection of tag dictionaries (one per metadata directory).
    /// Using simple dictionaries decouples callers from MetadataExtractor types and allows ExifTool fallback.
    /// </summary>
    public interface IMetadataReader
    {
        /// <summary>
        /// Read metadata for the given file path. Returns a list of directories, each represented as a map tagName->value.
        /// </summary>
        IReadOnlyList<IDictionary<string, string>> Read(string filePath);
    }
}
