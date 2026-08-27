using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoArchive;

internal enum TransferStage
{
    Preparing,
    Copying,
    Organizing,
    Verifying,
    Completed,
    Cancelled,
    Failed
}

internal sealed record TransferSnapshot(
    TransferStage Stage,
    int TotalFiles,
    int CompletedFiles,
    long TotalBytes,
    long CompletedBytes,
    string CurrentFile,
    string CurrentFolder,
    double BytesPerSecond,
    TimeSpan? Remaining,
    int Errors,
    string? Error = null)
{
    public double Fraction => TotalBytes > 0
        ? Math.Clamp(CompletedBytes / (double)TotalBytes, 0, 1)
        : TotalFiles > 0 ? Math.Clamp(CompletedFiles / (double)TotalFiles, 0, 1) : 0;
}

internal sealed class TransferService
{
    static readonly Regex DatePattern = new(@"(?<y>20\d{2})[-_]?((?<m>0[1-9]|1[0-2]))[-_]?(?<d>0[1-9]|[12]\d|3[01])", RegexOptions.Compiled);

    public async Task RunAsync(
        string serial,
        IReadOnlyList<MediaItem> files,
        string destination,
        IProgress<TransferSnapshot> progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);
        var sizes = new long[files.Count];
        long totalBytes = 0;
        var errors = 0;

        progress.Report(new(TransferStage.Preparing, files.Count, 0, 0, 0, "Подготавливаем список", destination, 0, null, 0));
        for (var i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var raw = await Adb.RunAsync(cancellationToken, "-s", serial, "shell", "stat", "-c", "%s", files[i].Remote);
                long.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out sizes[i]);
                totalBytes += Math.Max(0, sizes[i]);
            }
            catch { sizes[i] = 0; }
            progress.Report(new(TransferStage.Preparing, files.Count, i, totalBytes, 0, files[i].Name, destination, 0, null, errors));
        }

        var overall = Stopwatch.StartNew();
        long completedBytes = 0;
        for (var index = 0; index < files.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = files[index];
            var datedFolder = ResolveDatedFolder(destination, item.Name);
            Directory.CreateDirectory(datedFolder);
            var outputPath = UniquePath(Path.Combine(datedFolder, item.Name));
            long fileBytes = 0;

            try
            {
                using var process = Adb.StartProcess("-s", serial, "exec-out", "cat", item.Remote);
                process.Start();
                await using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var buffer = new byte[1024 * 1024];
                while (true)
                {
                    var read = await process.StandardOutput.BaseStream.ReadAsync(buffer, cancellationToken);
                    if (read == 0) break;
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    fileBytes += read;
                    var speed = (completedBytes + fileBytes) / Math.Max(0.2, overall.Elapsed.TotalSeconds);
                    TimeSpan? remaining = speed > 0 && totalBytes > 0
                        ? TimeSpan.FromSeconds(Math.Max(0, totalBytes - completedBytes - fileBytes) / speed)
                        : null;
                    progress.Report(new(TransferStage.Copying, files.Count, index, totalBytes, completedBytes + fileBytes, item.Name, datedFolder, speed, remaining, errors));
                }
                await output.FlushAsync(cancellationToken);
                var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);
                if (process.ExitCode != 0) throw new IOException(string.IsNullOrWhiteSpace(stderr) ? $"ADB завершился с кодом {process.ExitCode}" : stderr.Trim());
                completedBytes += fileBytes;
            }
            catch (OperationCanceledException)
            {
                TryDelete(outputPath);
                progress.Report(new(TransferStage.Cancelled, files.Count, index, totalBytes, completedBytes, item.Name, datedFolder, 0, null, errors));
                throw;
            }
            catch (Exception ex)
            {
                errors++;
                TryDelete(outputPath);
                progress.Report(new(TransferStage.Copying, files.Count, index + 1, totalBytes, completedBytes, item.Name, datedFolder, 0, null, errors, ex.Message));
            }

            var currentSpeed = completedBytes / Math.Max(0.2, overall.Elapsed.TotalSeconds);
            TimeSpan? eta = currentSpeed > 0 && totalBytes > 0
                ? TimeSpan.FromSeconds(Math.Max(0, totalBytes - completedBytes) / currentSpeed)
                : null;
            progress.Report(new(TransferStage.Organizing, files.Count, index + 1, totalBytes, completedBytes, item.Name, datedFolder, currentSpeed, eta, errors));
        }

        progress.Report(new(TransferStage.Verifying, files.Count, files.Count, totalBytes, completedBytes, "Проверяем результат", destination, completedBytes / Math.Max(.2, overall.Elapsed.TotalSeconds), TimeSpan.Zero, errors));
        await Task.Delay(250, cancellationToken);
        progress.Report(new(TransferStage.Completed, files.Count, files.Count, totalBytes, completedBytes, "Готово", destination, completedBytes / Math.Max(.2, overall.Elapsed.TotalSeconds), TimeSpan.Zero, errors));
    }

    static string ResolveDatedFolder(string root, string name)
    {
        var match = DatePattern.Match(name);
        if (!match.Success) return Path.Combine(root, "Без даты");
        return Path.Combine(root, match.Groups["y"].Value, match.Groups["m"].Value, match.Groups["d"].Value);
    }

    static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(directory, $"{name} ({i}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
