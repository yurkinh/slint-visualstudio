namespace Slint.VisualStudio.Core.Server;

public static class ServerEnvironment
{
    public const string PanicLogDirectoryVariable = "SLINT_LSP_PANIC_LOG_DIR";

    private static readonly string[] LogLevels = ["info", "debug", "trace"];

    public static IReadOnlyDictionary<string, string> Build(string logLevel, string panicLogDirectory) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["RUST_LOG"] = "slint_lsp=" + NormalizeLogLevel(logLevel),
            [PanicLogDirectoryVariable] = panicLogDirectory,
        };

    public static string NormalizeLogLevel(string? logLevel)
    {
        var normalized = logLevel?.Trim().ToLowerInvariant();
        return normalized is not null && LogLevels.Contains(normalized) ? normalized : "info";
    }
}
