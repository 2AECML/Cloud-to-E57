using System.Diagnostics;
using System.Text;
using System.Text.Json;
using EasyTry.Conversion.Models;

namespace EasyTry.Conversion.Services;

internal sealed class ConversionWorkerClient
{
    private readonly Func<ProcessStartInfo> startInfoFactory;
    private readonly TimeSpan cancellationGrace;

    internal ConversionWorkerClient()
        : this(CreateStartInfo, TimeSpan.FromSeconds(10)) { }

    internal ConversionWorkerClient(Func<ProcessStartInfo> startInfoFactory, TimeSpan cancellationGrace)
    {
        this.startInfoFactory = startInfoFactory;
        this.cancellationGrace = cancellationGrace;
    }

    internal async Task<ConversionReport> ConvertAsync(
        ConversionOptions options,
        IProgress<ConversionProgress>? progress,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var oldTemporaryFiles = GetTemporaryFiles(options).ToHashSet(StringComparer.OrdinalIgnoreCase);
        using var lifetime = new WorkerProcessLifetime();
        using var process = new Process { StartInfo = Prepare(startInfoFactory()) };
        var started = false;
        var succeeded = false;
        var errorOutput = new StringBuilder();
        WorkerMessage? result = null;
        string? workerError = null;
        Task? cancellation = null;
        Task? stdout = null;
        Task? stderr = null;
        try
        {
            if (!process.Start()) { throw new InvalidOperationException("无法启动转换组件。"); }
            started = true;
            lifetime.Attach(process);
            stdout = ReadOutputAsync();
            stderr = ReadErrorAsync();
            await process.StandardInput.WriteLineAsync(
                JsonSerializer.Serialize(options, ConversionWorkerProtocol.Json)).ConfigureAwait(false);
            await process.StandardInput.FlushAsync().ConfigureAwait(false);
            using var registration = token.Register(() => cancellation = StopWorkerAsync(process));
            var exit = process.WaitForExitAsync();
            var first = await Task.WhenAny(exit, stdout, stderr).ConfigureAwait(false);
            if (first.IsFaulted) { await first.ConfigureAwait(false); }
            await exit.ConfigureAwait(false);
            // Wait for a concurrently invoked cancellation callback to publish
            // its cleanup task before disposing any process or pipe handles.
            registration.Dispose();
            if (cancellation is not null) { await cancellation.ConfigureAwait(false); }
            // Also release any descendant that inherited stdout/stderr, so it
            // cannot keep the pipe readers waiting after this worker exits.
            lifetime.Dispose();
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0 || result?.Report is null || workerError is not null)
            {
                var detail = workerError ?? errorOutput.ToString().Trim();
                var code = unchecked((uint)process.ExitCode).ToString("X8");
                throw new InvalidOperationException(
                    $"此项转换组件异常退出（0x{code}），其余任务可继续。" +
                    (detail.Length == 0 ? "未收到完整转换结果。" : detail));
            }
            succeeded = true;
            return result.Report;

            async Task ReadOutputAsync()
            {
                while (await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
                {
                    if (!line.StartsWith(ConversionWorkerProtocol.Prefix, StringComparison.Ordinal)) { continue; }
                    try
                    {
                        var message = JsonSerializer.Deserialize<WorkerMessage>(
                            line[ConversionWorkerProtocol.Prefix.Length..], ConversionWorkerProtocol.Json);
                        if (message?.Kind == "progress" && message.Progress is not null)
                            progress?.Report(message.Progress);
                        else if (message?.Kind == "result") result = message;
                        else if (message?.Kind == "error") workerError = message.Error;
                    }
                    catch (JsonException) { workerError = "转换组件返回了无效的进度或结果。"; }
                }
            }

            async Task ReadErrorAsync()
            {
                var buffer = new char[1024];
                int count;
                while ((count = await process.StandardError.ReadAsync(buffer).ConfigureAwait(false)) > 0)
                {
                    errorOutput.Append(buffer, 0, count);
                    if (errorOutput.Length > 8192) errorOutput.Remove(0, errorOutput.Length - 8192);
                }
            }
        }
        finally
        {
            if (started)
            {
                if (!process.HasExited)
                {
                    try { process.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) when (process.HasExited) { }
                }
                await process.WaitForExitAsync().ConfigureAwait(false);
                lifetime.Dispose();
                if (stdout is not null && stderr is not null)
                {
                    try { await Task.WhenAll(stdout, stderr).ConfigureAwait(false); }
                    catch { /* Retain the original pipe/conversion exception. */ }
                }
                // Only the owned task's newly created incomplete files are
                // removed. Completed E57s and earlier partial files stay intact.
                if (!succeeded)
                {
                    string[] temporary;
                    try { temporary = GetTemporaryFiles(options).Except(oldTemporaryFiles, StringComparer.OrdinalIgnoreCase).ToArray(); }
                    catch (IOException) { temporary = []; }
                    catch (UnauthorizedAccessException) { temporary = []; }
                    foreach (var path in temporary)
                    {
                        try { File.Delete(path); }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                }
            }
        }
    }

    private async Task StopWorkerAsync(Process process)
    {
        try
        {
            await process.StandardInput.WriteLineAsync("cancel").ConfigureAwait(false);
            await process.StandardInput.FlushAsync().ConfigureAwait(false);
        }
        catch (IOException) { }
        catch (InvalidOperationException) { }
        var exit = process.WaitForExitAsync();
        if (await Task.WhenAny(exit, Task.Delay(cancellationGrace)).ConfigureAwait(false) != exit && !process.HasExited)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) when (process.HasExited) { }
        }
        await exit.ConfigureAwait(false);
    }

    private static ProcessStartInfo CreateStartInfo()
    {
        var directory = Path.GetDirectoryName(typeof(PointCloudConversionService).Assembly.Location)!;
        var executable = Path.Combine(directory, "EasyTry.exe");
        if (!File.Exists(executable))
            throw new FileNotFoundException("缺少独立转换组件，请重新构建或完整部署程序。", executable);
        var start = new ProcessStartInfo(executable) { WorkingDirectory = directory };
        start.ArgumentList.Add(ConversionWorkerProtocol.Argument);
        return start;
    }

    private static ProcessStartInfo Prepare(ProcessStartInfo start)
    {
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.StandardInputEncoding = new UTF8Encoding(false);
        start.StandardOutputEncoding = new UTF8Encoding(false);
        start.StandardErrorEncoding = new UTF8Encoding(false);
        return start;
    }

    private static IEnumerable<string> GetTemporaryFiles(ConversionOptions options)
    {
        var output = Path.GetFullPath(options.OutputFilePath);
        var directory = Path.GetDirectoryName(output)!;
        if (Directory.Exists(directory))
            foreach (var path in Directory.EnumerateFiles(directory, $".{Path.GetFileName(output)}.*.writing")) yield return path;
        var parts = Path.Combine(ConversionOutputLayout.GetDatasetDirectory(output), "parts");
        if (Directory.Exists(parts))
            foreach (var path in Directory.EnumerateFiles(parts, "*.writing")) yield return path;
    }
}
