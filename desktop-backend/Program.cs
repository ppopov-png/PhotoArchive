using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using MediaDevices;

var pipeName = args.FirstOrDefault() ?? "PhotoArchive-Media";
var deviceSerial = args.Skip(1).FirstOrDefault() ?? "default";
using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
await pipe.WaitForConnectionAsync();
using var writer = new StreamWriter(pipe) { AutoFlush = false, NewLine = "\n" };
try
{
    var cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoArchive");
    Directory.CreateDirectory(cacheDir);
    // ADB gives a streaming directory listing in one USB session. This avoids
    // the per-object Windows MTP/WPD round trips that make large libraries slow.
    var adbPaths = !string.IsNullOrWhiteSpace(deviceSerial) ? await StreamAdbMedia(writer, deviceSerial) : null;
    if (adbPaths != null)
    {
        await writer.WriteLineAsync("DONE");
        await writer.FlushAsync();
        var previewDevice = MediaDeviceManager.Instance.GetDevices().FirstOrDefault();
        if (previewDevice != null)
        {
            using (previewDevice) { previewDevice.Connect(); await SendThumbnailsFromMtp(writer, previewDevice, adbPaths, cacheDir, true); previewDevice.Disconnect(); }
        }
        return;
    }

    var device = MediaDeviceManager.Instance.GetDevices().FirstOrDefault();
    if (device == null) { await writer.WriteLineAsync("ERROR\tMTP device not found"); return; }
    using (device) {
        device.Connect();
        var current = new System.Collections.Generic.List<string>();
        foreach (var path in device.EnumerateFiles("/", "*", SearchOption.AllDirectories))
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(path, @"\.(jpg|jpeg|png|webp|heic|gif|mp4|mov|mkv|webm|3gp)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
            current.Add(path);
            await writer.WriteLineAsync("FILE\t" + path);
        }
        await writer.WriteLineAsync("TOTAL\t" + current.Count);
        foreach (var path in current) await writer.WriteLineAsync("FILE\t" + path);
        await writer.WriteLineAsync("DONE");
        await writer.FlushAsync();
        await SendThumbnailsFromMtp(writer, device, current, cacheDir, false);
        device.Disconnect();
    }
}
catch (Exception ex) { try { await writer.WriteLineAsync("ERROR\t" + ex.Message.Replace('\t', ' ')); } catch { } }

static async Task<System.Collections.Generic.List<string>?> StreamAdbMedia(StreamWriter writer, string serial)
{
    var adb = new[]
    {
        @"C:\Android\Sdk\platform-tools\adb.exe",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "platform-tools", "adb.exe"),
        "adb.exe"
    }.FirstOrDefault(x => x == "adb.exe" || File.Exists(x));
    try
    {
        var start = new ProcessStartInfo(adb!) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add("-s"); start.ArgumentList.Add(serial);
        start.ArgumentList.Add("shell");
        start.ArgumentList.Add("find /storage/emulated/0 -type f \\( -iname '*.jpg' -o -iname '*.jpeg' -o -iname '*.png' -o -iname '*.webp' -o -iname '*.heic' -o -iname '*.gif' -o -iname '*.mp4' -o -iname '*.mov' -o -iname '*.mkv' -o -iname '*.webm' -o -iname '*.3gp' \\)");
        using var process = Process.Start(start);
        if (process == null) return null;
        var paths = new System.Collections.Generic.List<string>();
        var sent = 0;
        while (await process.StandardOutput.ReadLineAsync() is { } line)
        {
            line = line.Trim();
            if (System.Text.RegularExpressions.Regex.IsMatch(line, @"\.(jpg|jpeg|png|webp|heic|gif|mp4|mov|mkv|webm|3gp)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                paths.Add(line);
                if (paths.Count % 256 == 0)
                {
                    await writer.WriteLineAsync("COUNTING\t" + paths.Count);
                    await writer.FlushAsync();
                }
            }
        }
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) return null;
        await writer.WriteLineAsync("TOTAL\t" + paths.Count);
        foreach (var path in paths)
        {
            await writer.WriteLineAsync("FILE\t" + path);
            if (++sent % 256 == 0) await writer.FlushAsync();
        }
        await writer.FlushAsync();
        return paths;
    }
    catch { return null; }
}

static async Task SendThumbnailsFromMtp(StreamWriter writer, MediaDevice device, System.Collections.Generic.List<string> paths, string cacheDir, bool adbPaths)
{
    foreach (var path in paths)
    {
        try
        {
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path))).ToLowerInvariant();
            var cacheFile = Path.Combine(cacheDir, key + ".bin");
            byte[] bytes;
            if (File.Exists(cacheFile)) bytes = await File.ReadAllBytesAsync(cacheFile);
            else
            {
                using var thumb = new MemoryStream();
                var mtpPath = adbPaths && path.StartsWith("/storage/emulated/0", StringComparison.Ordinal)
                    ? "\\Внутреннее хранилище" + path["/storage/emulated/0".Length..].Replace('/', '\\')
                    : path;
                device.DownloadThumbnail(mtpPath, thumb);
                bytes = thumb.ToArray();
                if (bytes.Length > 0 && bytes.Length <= 4 * 1024 * 1024) await File.WriteAllBytesAsync(cacheFile, bytes);
            }
            if (bytes.Length > 0 && bytes.Length <= 4 * 1024 * 1024)
                await writer.WriteLineAsync("THUMB\t" + path + "\t" + Convert.ToBase64String(bytes));
        }
        catch (Exception ex) { await writer.WriteLineAsync("THUMB_ERROR\t" + path + "\t" + ex.Message.Replace('\t', ' ')); }
        await writer.FlushAsync();
    }
}
