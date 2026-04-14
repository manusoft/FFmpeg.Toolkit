using ManuHub.FF.NET.Abstractions;
using ManuHub.FF.NET.Core;
using ManuHub.FF.NET.Models;
using ManuHub.FF.NET.Parsing;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace ManuHub.FF.NET.Builders;

public class ProbeBuilder
{
    private readonly IFFmpegRunner _runner;
    private readonly FFmpegOptions _options;
    private readonly FFmpegCommandBuilder _cmd;

    private string? _inputPath;
    private MediaInfo? _probedInfo;

    public ProbeBuilder(IFFmpegRunner runner, FFmpegOptions? options = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _options = options?.Clone() ?? new FFmpegOptions();
        _cmd = new FFmpegCommandBuilder(_options);
    }

    // ───────────────────────────────────────────────
    // Input path
    // ───────────────────────────────────────────────
    public ProbeBuilder From(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            throw new ArgumentException("Input path cannot be empty", nameof(inputPath));

        _inputPath = inputPath;
        _cmd.Input(inputPath);
        return this;
    }

    // ───────────────────────────────────────────────
    // Probe
    // ───────────────────────────────────────────────
    public async Task<MediaInfo?> ExcecuteAsync(CancellationToken ct = default)
    {
        if (_inputPath is null)
            throw new InvalidOperationException("Input must be set using .From(...) before probing.");

        if (_probedInfo is not null)
            return _probedInfo;

        var ffprobeRunner = new FFprobeRunner(_runner, _options);

        try
        {
            string json = await ffprobeRunner.GetJsonOutputAsync(_inputPath, ct: ct);
            _probedInfo = JsonSerializer.Deserialize<MediaInfo>(json); //MediaInfoParser.Parse(json);
            if (_probedInfo == null) return null;

            _options.Logger?.LogInformation(
                "Probed: {duration:F1}s | Video: {vcodec} {w}x{h} | Audio: {acodec}",
                _probedInfo.Duration.TotalSeconds,
                _probedInfo.VideoStream?.CodecName ?? "–",
                _probedInfo.VideoStream?.Width ?? 0,
                _probedInfo.VideoStream?.Height ?? 0,
                _probedInfo.AudioStream?.CodecName ?? "–");

            return _probedInfo;
        }
        catch (Exception ex)
        {
            _options.Logger?.LogWarning(ex, "Failed to probe input {Input}", _inputPath);
            throw;
        }
    }
}
