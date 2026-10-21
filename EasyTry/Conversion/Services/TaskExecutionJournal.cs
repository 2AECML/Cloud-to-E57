namespace EasyTry.Conversion.Services;

public sealed class TaskExecutionJournal
{
    private readonly object sync = new();

    public TaskExecutionJournal(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
    }

    public string Path { get; }

    public void Append(
        string status,
        string input,
        string output,
        string message = "")
    {
        var cleanMessage = message
            .Replace('\t', ' ')
            .Replace('\r', ' ')
            .Replace('\n', ' ');
        var line = string.Join('\t',
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            status,
            input,
            output,
            cleanMessage) + Environment.NewLine;
        lock (sync)
        {
            File.AppendAllText(Path, line, new System.Text.UTF8Encoding(true));
        }
    }

    public HashSet<string> ReadCompletedKeys()
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(Path))
        {
            return keys;
        }

        foreach (var line in File.ReadLines(Path))
        {
            var columns = line.Split('\t');
            if (columns.Length >= 4 && columns[1] == "SUCCESS")
            {
                keys.Add(BuildKey(columns[2], columns[3]));
            }
        }
        return keys;
    }

    public static string BuildKey(string input, string output) =>
        $"{System.IO.Path.GetFullPath(input)}\n{System.IO.Path.GetFullPath(output)}";
}

