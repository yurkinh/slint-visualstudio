namespace Slint.VisualStudio.Core.Server;

/// <summary>Panic report slint-lsp writes into <c>SLINT_LSP_PANIC_LOG_DIR</c> before it dies.</summary>
public sealed record PanicLog(string Version, string Location, string Backtrace, string Message)
{
    public const string FilePattern = "slint_lsp_panic_*.log";

    public static PanicLog? Parse(string content)
    {
        var lines = content.ReplaceLineEndings("\n").Split('\n');
        if (lines.Length < 4)
        {
            return null;
        }

        return new PanicLog(lines[0], lines[1], lines[2], string.Join('\n', lines[3..]).TrimEnd());
    }

    /// <summary>Returns the newest panic log in <paramref name="directory"/> and deletes all of them.</summary>
    public static PanicLog? TakeLatest(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        var files = new DirectoryInfo(directory)
            .GetFiles(FilePattern)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToList();

        PanicLog? latest = null;
        foreach (var file in files)
        {
            try
            {
                latest ??= Parse(File.ReadAllText(file.FullName));
                file.Delete();
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return latest;
    }
}
