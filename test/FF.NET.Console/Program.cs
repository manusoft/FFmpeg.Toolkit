
using ManuHub.FF.NET;

Console.WriteLine("Hello, World!");

var input = @"C:\Users\manua\Videos\Anu.mp4";
var output = @"C:\downloads\Output_Anu.mp4";

// Configure (use absolute paths!)
FFmpegToolkit.Configure(opt =>
{
    opt.FFmpegPath = @"Tools\ffmpeg.exe";   // ← full absolute path recommended
    opt.FFprobePath = @"Tools\ffprobe.exe";
    opt.TempDirectory = Path.Combine(Path.GetTempPath(), "ManuHubFFmpeg");
});

var progress = new Progress<FFmpegProgress>(p =>
{
    string percent = p.PercentComplete.HasValue
        ? $"{p.PercentComplete:F1}%"
        : "??%";

    string timeStr = p.Time.HasValue
        ? p.Time.Value.ToString(@"hh\:mm\:ss")
        : "--:--:--";

    string speedStr = p.Speed?.TrimEnd('x') ?? "N/A";

    Console.WriteLine($"Progress: {percent} | Time: {timeStr} | Speed: {speedStr}x");
});

Console.WriteLine("Starting conversion...");

var result = await FFmpegToolkit
    .Convert()
    .From(input)
    .To(output)
    .Scale(1280, 720)   // you had :flags=lanczos
    .Crf(22)
    .WithProgress(progress)
    .ExecuteAsync();

Console.WriteLine($"Success: {result.Success}");
Console.WriteLine($"Duration: {result.Duration.TotalSeconds:F1} seconds");
if (!result.Success)
    Console.WriteLine($"Error: {result.FailureReason}");