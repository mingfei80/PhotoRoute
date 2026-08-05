using System.Text.RegularExpressions;

namespace UniversalMediaSorter.Utilities
{
    /// <summary>
    /// File and folder naming utility methods.
    /// </summary>
    public static class FileUtilities
    {
        /// <summary>
        /// Sanitizes a folder name by removing or replacing invalid characters.
        /// </summary>
        public static string SanitizeFolderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "UnknownLocation";

            // Replace invalid filename characters with underscores
            var invalidChars = new Regex($"[{Regex.Escape(new string(Path.GetInvalidFileNameChars()))}]");
            var sanitized = invalidChars.Replace(name, "_");

            // Replace common problematic characters
            sanitized = sanitized.Replace(" - ", " ").Replace("  ", " ").Trim();

            return string.IsNullOrWhiteSpace(sanitized) ? "UnknownLocation" : sanitized;
        }

        /// <summary>
        /// Returns a safe destination path, incrementing the filename if it already exists.
        /// </summary>
        public static string GetSafeDestination(string path)
        {
            if (!File.Exists(path))
                return path;

            var dir = Path.GetDirectoryName(path) ?? throw new InvalidOperationException($"Invalid path: {path}");
            var fileName = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            int counter = 1;

            while (File.Exists(path))
            {
                var newFileName = $"{fileName}_{counter}{ext}";
                path = Path.Combine(dir, newFileName);
                counter++;
            }

            return path;
        }

        /// <summary>
        /// Dumps metadata from a file to the console (for debugging).
        /// </summary>
        public static void DumpMetadata(string path)
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"File not found: {path}");
                return;
            }

            try
            {
                var dirs = MetadataExtractor.ImageMetadataReader.ReadMetadata(path);
                foreach (var d in dirs)
                {
                    Console.WriteLine($"[{d.Name}]");
                    foreach (var tag in d.Tags)
                    {
                        Console.WriteLine($"  {tag.Name}: {tag.Description ?? "(null)"}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error reading metadata: {ex.Message}");
            }
        }
    }
}
