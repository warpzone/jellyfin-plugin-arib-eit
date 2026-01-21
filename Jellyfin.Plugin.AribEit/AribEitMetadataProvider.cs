using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AribEit
{
    public class AribEitMetadataProvider :
        ILocalMetadataProvider<Movie>,
        ILocalMetadataProvider<Episode>,
        IHasOrder
    {
        private readonly ILogger<AribEitMetadataProvider> _logger;

        public AribEitMetadataProvider(ILogger<AribEitMetadataProvider> logger)
        {
            _logger = logger;
        }

        public string Name => "Embedded ARIB EIT";
        
        public int Order => 0;

        public bool CanHelp(BaseItem item)
        {
            return item is Movie || item is Episode;
        }

        Task<MetadataResult<Movie>> ILocalMetadataProvider<Movie>.GetMetadata(ItemInfo info, IDirectoryService directoryService, CancellationToken cancellationToken)
        {
            return ProcessMetadata<Movie>(info, cancellationToken);
        }

        Task<MetadataResult<Episode>> ILocalMetadataProvider<Episode>.GetMetadata(ItemInfo info, IDirectoryService directoryService, CancellationToken cancellationToken)
        {
            return ProcessMetadata<Episode>(info, cancellationToken);
        }

        private async Task<MetadataResult<T>> ProcessMetadata<T>(ItemInfo info, CancellationToken cancellationToken)
            where T : BaseItem, new()
        {
            var result = new MetadataResult<T>();
            var path = info.Path;

            if (string.IsNullOrEmpty(path)) return result;

            var ext = Path.GetExtension(path) ?? string.Empty;

            var tsExtensions = new[] { ".ts", ".m2ts", ".m2t", ".mts" };

            bool isTsFile = tsExtensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase));

            if (!isTsFile)
            {
                return result;
            }

            _logger.LogInformation("parsing: {0}", path);

            try
            {
                var eitData = await ExecuteExternalEitCommandAsync(path, cancellationToken).ConfigureAwait(false);

                if (eitData != null)
                {
                    result.Item = new T
                    {
                        Name = eitData.Title,
                        Overview = eitData.Description,
                        PremiereDate = eitData.StartDate,
                        ProductionYear = eitData.StartDate?.Year
                    };

                    if (eitData.Genres.Count > 0)
                    {
                        result.Item.Genres = eitData.Genres.ToArray();
                    }

                    if (eitData.Tags.Count > 0)
                    {
                        result.Item.Tags = eitData.Tags.ToArray();
                    }

                    result.HasMetadata = true;
                    _logger.LogInformation("title applied: {0}", eitData.Title);
                } else {
                    _logger.LogInformation("meta data seems to be null: {0}", path);
		}
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "error occurred during processing: {0}", path);
            }

            return result;
        }

        private async Task<EitResult?> ExecuteExternalEitCommandAsync(string filePath, CancellationToken cancellationToken)
        {
            var fullCommand = Plugin.Instance?.Configuration.ExternalCommandPath;
            if (string.IsNullOrWhiteSpace(fullCommand)) return null;

            var parts = fullCommand.Split(' ', 2);
            var fileName = parts[0];
            var baseArgs = parts.Length > 1 ? parts[1] : string.Empty;
            var arguments = $"{baseArgs} \"{filePath}\"";

            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using var process = new Process { StartInfo = startInfo };
                process.Start();

                string output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                string error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

                if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                {
                    if (!string.IsNullOrWhiteSpace(error)) _logger.LogError("error occurred during running external program: {0}", error);
                    return null;
                }

                using var jsonDoc = JsonDocument.Parse(output);
                var root = jsonDoc.RootElement;

                var result = new EitResult();

                if (root.TryGetProperty("channel", out var channel))
                {
                    if (channel.TryGetProperty("channel_name", out var cn))
                    {
                        var name = cn.GetString();
                        if (!string.IsNullOrEmpty(name)) result.Tags.Add(name);
                    }
                }

                if (!root.TryGetProperty("program", out var program)) return result;

                result.Title = program.TryGetProperty("title", out var t) ? t.GetString() : "Unknown Title";

                var desc = program.TryGetProperty("description", out var d) ? d.GetString() : "";
                if (program.TryGetProperty("detail", out var detail))
                {
                    foreach (var property in detail.EnumerateObject())
                    {
                        desc += $"\n\n【{property.Name}】\n{property.Value.GetString()}";
                    }
                }
                result.Description = desc;

                if (program.TryGetProperty("start_time", out var st) && DateTime.TryParse(st.GetString(), out var date))
                {
                    result.StartDate = date;
                }

                if (program.TryGetProperty("genre", out var genreArray) && genreArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var g in genreArray.EnumerateArray())
                    {
                        string major = g.TryGetProperty("major", out var maj) ? maj.GetString() ?? "" : "";
                        string middle = g.TryGetProperty("middle", out var mid) ? mid.GetString() ?? "" : "";

                        if (!string.IsNullOrEmpty(major))
                        {
                            result.Genres.Add(!string.IsNullOrEmpty(middle) ? $"{major}/{middle}" : major);
                        }
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError("exception occurred during running external program: {0}", ex);
                return null;
            }
        }
    }

    public class EitResult
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public DateTime? StartDate { get; set; }
        public List<string> Genres { get; set; } = new List<string>();
        public List<string> Tags { get; set; } = new List<string>();
    }
}
