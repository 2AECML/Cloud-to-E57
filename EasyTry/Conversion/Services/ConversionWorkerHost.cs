using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using EasyTry.Conversion.Models;

namespace EasyTry.Conversion.Services;

/// <summary>One logical conversion per process; never constructs a window.</summary>
internal static class ConversionWorkerHost
{
    internal static int Run() => Run(PointCloudConversionService.ConvertInWorker);

    internal static int Run(
        Func<ConversionOptions, IProgress<ConversionProgress>, CancellationToken, ConversionReport> convert)
    {
        // An SDK access violation must exit this worker, not open a fault dialog
        // or corrupt the GUI's heap. The parent observes its exit code.
        SetErrorMode(0x0001 | 0x0002 | 0x8000);
        // This is a WinExe without a console. Setting Console.InputEncoding
        // calls SetConsoleCP and fails even when redirected pipe handles exist.
        // Bind readers/writers directly to those pipes instead.
        var utf8 = new UTF8Encoding(false);
        Console.SetIn(new StreamReader(Console.OpenStandardInput(), utf8));
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true });
        using var cancellation = new CancellationTokenSource();
        void Send(WorkerMessage message)
        {
            Console.Out.WriteLine(ConversionWorkerProtocol.Encode(message));
            Console.Out.Flush();
        }
        try
        {
            var request = Console.In.ReadLine()
                ?? throw new InvalidDataException("未收到转换任务。");
            var options = JsonSerializer.Deserialize<ConversionOptions>(request, ConversionWorkerProtocol.Json)
                ?? throw new InvalidDataException("转换任务为空。");
            _ = Task.Run(() =>
            {
                try
                {
                    while (Console.In.ReadLine() is { } command)
                    {
                        if (command == "cancel") { cancellation.Cancel(); return; }
                    }
                    // Parent disappearance closes stdin. The job object also
                    // terminates an unresponsive native SDK in this case.
                    cancellation.Cancel();
                }
                catch (ObjectDisposedException) { }
                catch (IOException) { }
            });
            var progress = new InlineProgress(value => Send(new("progress", Progress: value)));
            var report = convert(options, progress, cancellation.Token);
            Send(new("result", Report: report));
            return 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Send(new("cancelled"));
            return 2;
        }
        catch (Exception error)
        {
            Send(new("error", Error: error.Message));
            return 1;
        }
    }

    private sealed class InlineProgress(Action<ConversionProgress> callback) : IProgress<ConversionProgress>
    {
        public void Report(ConversionProgress value) => callback(value);
    }

    [DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint mode);
}
