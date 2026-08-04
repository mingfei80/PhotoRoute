using MetadataExtractor;
using MetadataExtractor.Formats.QuickTime;

namespace UniversalMediaSorter.Metadata
{
    /// <summary>
    /// Uses MetadataExtractor to read metadata and converts directories to dictionaries.
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
                    foreach (var tag in d.Tags)
                    {
                        try { map[tag.Name] = tag.Description ?? string.Empty; } catch { }
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
