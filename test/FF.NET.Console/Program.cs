
using ManuHub.FF.NET;

Console.WriteLine("Hello, World!");

var input = @"C:\Users\manua\Videos\Anu.mp4";
var output = @"C:\downloads\Output_Anu.mp4";
var temp = @"C:\downloads\temp";

// Configure (use absolute paths!)
FFmpegToolkit.Configure(opt =>
{
    opt.FFmpegPath = @"Tools\ffmpeg.exe";   // ← full absolute path recommended
    opt.FFprobePath = @"Tools\ffprobe.exe";
    opt.TempDirectory = temp; //Path.Combine(Path.GetTempPath(), "ManuHubFFmpeg");
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

//Console.WriteLine("Starting conversion...");

//var convertResult = await FFmpegToolkit
//    .Convert()
//    .From(input)
//    .To(output)
//    //.Scale(1280, 720)   // you had :flags=lanczos
//    .Crf(22)
//    .WithProgress(progress)
//    .ExecuteAsync();

//Console.WriteLine($"Success: {convertResult.Success}");
//Console.WriteLine($"Duration: {convertResult.Duration.TotalSeconds:F1} seconds");
//if (!convertResult.Success)
//    Console.WriteLine($"Error: {convertResult.FailureReason}");

Console.WriteLine("Starting probe info...");
var probeResult = await FFmpegToolkit
    .Probe()
    .From(@"D:\Movies\Telegram Desktop\Kill (2023) HINDI.mkv")
    .ExcecuteAsync();

Console.WriteLine($"Success: {probeResult is not null}");

Console.WriteLine("Starting thumbnails...");
var result1 = await FFmpegToolkit
    .Thumbnails()
    .From(@"D:\Movies\Telegram Desktop\Kill (2023) HINDI.mkv")

    //.Every(TimeSpan.FromMinutes(5)) // Every 5 minutes
    //.KeyframesOnly()
    //.OutputPattern(@"C:\downloads\frame_%04d.jpg") 
    .WithProgress(progress)
    .ExecuteAsync();

Console.WriteLine($"Success: {result1.Success}");
Console.WriteLine($"Duration: {result1.Duration.TotalSeconds:F1} seconds");
if (!result1.Success)
    Console.WriteLine($"Error: {result1.FailureReason}");