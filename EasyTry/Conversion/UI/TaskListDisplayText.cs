using EasyTry.Conversion.Models;
using EasyTry.Conversion.Services;

namespace EasyTry.Conversion.UI;

internal static class TaskListDisplayText
{
    public static string GetTlsdStatus(BatchConversionTask task)
    {
        var metadata = TlsdProjectMetadataReader.Read(task.InputPaths[0], task.TlsdFilePath);
        if (metadata.FilePath is not null)
        {
            return File.Exists(metadata.FilePath) ? "有 tlsd" : "tlsd 路径失效";
        }
        return metadata.HasWarning ? "tlsd 待确认" : "无 tlsd";
    }

    public static string FormatTaskPath(BatchConversionTask task) =>
        $"[{GetTlsdStatus(task)}] {task.InputRoot}  →  {task.OutputDirectory}";
}
