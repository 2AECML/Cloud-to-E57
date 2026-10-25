using EasyTry.Conversion.Models;

namespace EasyTry.Conversion.Services;

/// <summary>
/// Runs a bounded number of independent logical scans. Each callback owns its
/// SDK source and writer; overlapping inputs or outputs never run together.
/// Callbacks retain the caller's context so the conversion form can update UI.
/// </summary>
public sealed class BatchConversionScheduler
{
    public const int DefaultConcurrency = 2;
    public const int MaximumConcurrency = 4;

    public async Task RunAsync(
        IReadOnlyList<ConversionOptions> options,
        int maxConcurrency,
        Func<int, ConversionOptions, CancellationToken, Task> convert,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(convert);
        if (maxConcurrency < 1 || maxConcurrency > MaximumConcurrency)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency));
        }

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var resources = options.Select(GetResources).ToArray();
        var pending = Enumerable.Range(0, options.Count).ToList();
        var active = new Dictionary<int, Task>();

        try
        {
            while (pending.Count > 0 || active.Count > 0)
            {
                stop.Token.ThrowIfCancellationRequested();
                while (active.Count < maxConcurrency)
                {
                    var position = pending.FindIndex(index =>
                        active.Keys.All(running => !resources[index].Overlaps(resources[running])));
                    if (position < 0) { break; }
                    var index = pending[position];
                    pending.RemoveAt(position);
                    active.Add(index, convert(index, options[index], stop.Token));
                    // A synchronous callback may cancel or fail before yielding.
                    if (active[index].IsFaulted || active[index].IsCanceled)
                    {
                        await active[index];
                    }
                    stop.Token.ThrowIfCancellationRequested();
                }

                if (active.Count == 0) { break; }
                await Task.WhenAny(active.Values);
                foreach (var index in active.Keys.Where(index => active[index].IsCompleted).ToArray())
                {
                    var task = active[index];
                    active.Remove(index);
                    await task;
                }
            }
        }
        catch
        {
            // Do not return control to the form while a source/writer is still
            // using the run's cancellation token, output files, or SDK objects.
            stop.Cancel();
            try { await Task.WhenAll(active.Values); }
            catch { /* Preserve the original failure after all writers exit. */ }
            throw;
        }
    }

    private static HashSet<string> GetResources(ConversionOptions options)
    {
        var resources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in options.ContinuousInputPaths.Prepend(options.InputFilePath))
        {
            var input = string.Equals(Path.GetExtension(path), ".zfs", StringComparison.OrdinalIgnoreCase)
                ? ZfsVolumeSet.GetPrimaryPath(path)
                : path;
            resources.Add(Path.GetFullPath(input).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        resources.Add(Path.GetFullPath(options.OutputFilePath));
        resources.Add(ConversionOutputLayout.GetDatasetDirectory(options.OutputFilePath));
        return resources;
    }
}
