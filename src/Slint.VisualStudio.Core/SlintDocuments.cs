namespace Slint.VisualStudio.Core;

public static class SlintDocuments
{
    public static bool IsRust(string pathOrUri) => pathOrUri.EndsWith(".rs", StringComparison.OrdinalIgnoreCase);

    /// <summary>A Slint file always needs the server; a Rust file only when it uses the <c>slint!</c> macro.</summary>
    public static bool WarrantsServer(string pathOrUri, Func<string> getText, bool attachToRust) =>
        !IsRust(pathOrUri) || (attachToRust && getText().Contains("slint!", StringComparison.Ordinal));
}
