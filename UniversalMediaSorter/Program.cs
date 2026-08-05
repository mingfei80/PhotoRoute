using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UniversalMediaSorter.Location;
using UniversalMediaSorter.Metadata;
using UniversalMediaSorter.Processors;
using UniversalMediaSorter.Services;
using UniversalMediaSorter.Utilities;

namespace UniversalMediaSorter
{
    internal static class Program
    {
        // Entry point
        public static async Task<int> Main(string[] args)
        {
            try
            {
                var builder = new ConfigurationBuilder()
                    .SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("config.json", optional: true, reloadOnChange: false)
                    .AddJsonFile(Path.Combine(Environment.CurrentDirectory, "config.json"), optional: true, reloadOnChange: false);

                var configuration = builder.Build();
                string? source = configuration["Source"]; 
                string? output = configuration["Output"];

                // dump mode
                if (args.Length > 0 && args[0] == "--dump")
                {
                    if (args.Length < 2) { Console.Error.WriteLine("Usage: --dump <file>"); return 1; }
                    var target = args[1];
                    if (!File.Exists(target)) { Console.Error.WriteLine($"File not found: {target}"); return 2; }
                    FileUtilities.DumpMetadata(target);
                    return 0;
                }

                if (string.IsNullOrWhiteSpace(source)) source = args.Length > 0 ? args[0] : null;
                if (string.IsNullOrWhiteSpace(output)) output = args.Length > 1 ? args[1] : null;

                if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(output))
                {
                    Console.Error.WriteLine("Source and Output must be provided via config.json or command-line args.");
                    return 2;
                }

                if (!Directory.Exists(source)) { Console.Error.WriteLine($"Source not found: {source}"); return 3; }
                Directory.CreateDirectory(output);

                // Set up Dependency Injection
                var services = new ServiceCollection();
                services.AddSingleton<IMetadataReader, MetadataExtractorReader>();
                services.AddSingleton<ILocationExtractor, DictionaryLocationExtractor>();
                services.AddSingleton<IReverseGeocoder, ReverseGeocoder>();
                services.AddSingleton<MediaProcessor>();

                using (var serviceProvider = services.BuildServiceProvider())
                {
                    var processor = serviceProvider.GetRequiredService<MediaProcessor>();

                    var exts = new[] { ".jpg", ".jpeg", ".heic", ".heif", ".png", ".mp4", ".mov", ".m4v", ".3gp" };
                    var files = Directory.EnumerateFiles(source, "*.*", SearchOption.AllDirectories)
                        .Where(f => exts.Contains(Path.GetExtension(f)?.ToLowerInvariant()));

                    foreach (var file in files)
                    {
                        var (success, destPath, error) = await processor.ProcessFileAsync(file, output).ConfigureAwait(false);
                        if (success)
                        {
                            Console.WriteLine($"Copied: {file} -> {destPath}");
                        }
                        else
                        {
                            Console.Error.WriteLine($"Failed {file}: {error}");
                        }
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }
    }
}
