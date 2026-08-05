using System.Diagnostics;
using System.Text.Json;

namespace UniversalMediaSorter.Metadata
{
    /// <summary>
    /// Uses exiftool as a fallback for robust metadata extraction. Requires exiftool on PATH or configured path.
    /// Returns directories as dictionaries (the JSON output from exiftool is flattened per file).
    /// </summary>
    public class ExifToolMetadataReader : IMetadataReader
    {
        private readonly string _exifToolPath;

        public ExifToolMetadataReader(string? exifToolPath = null)
        {
            _exifToolPath = string.IsNullOrWhiteSpace(exifToolPath) ? "exiftool" : exifToolPath!;
        }

        public IReadOnlyList<IDictionary<string, string>> Read(string filePath)
        {
            try
            {
                var psi = new ProcessStartInfo(_exifToolPath, $"-j -G -a -n " + Quote(filePath))
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var p = Process.Start(psi);
                if (p == null) return Array.Empty<IDictionary<string, string>>();
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
                if (string.IsNullOrWhiteSpace(output)) return Array.Empty<IDictionary<string, string>>();

                // exiftool -j returns an array of objects (one per file). We'll parse first object.
                var json = JsonDocument.Parse(output);
                if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() == 0) return Array.Empty<IDictionary<string, string>>();

                var obj = json.RootElement[0];
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in obj.EnumerateObject())
                {
                    try { map[prop.Name] = prop.Value.ToString() ?? string.Empty; } catch { }
                }

                // Return single directory map
                return new[] { (IDictionary<string, string>)map };
            }
            catch
            {
                return Array.Empty<IDictionary<string, string>>();
            }
        }

        private static string Quote(string s) => '"' + s.Replace("\"", "\\\"") + '"';
    }
}
