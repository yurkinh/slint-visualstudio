using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Slint.VisualStudio.Core;

/// <summary>User-facing settings of the extension, mirroring the VS Code extension's <c>slint.*</c> settings.</summary>
public sealed partial record SlintConfiguration
{
    public string LspBinaryPath { get; init; } = "";

    public IReadOnlyList<string> LspArgs { get; init; } = [];

    public string LogLevel { get; init; } = "info";

    public IReadOnlyList<string> IncludePaths { get; init; } = [];

    /// <summary>Entries in the form <c>name=path</c>.</summary>
    public IReadOnlyList<string> LibraryPaths { get; init; } = [];

    public string PreviewStyle { get; init; } = "";

    public bool PreviewHideUi { get; init; }

    public bool AttachToRust { get; init; } = true;

    /// <summary>Builds the value slint-lsp expects for the <c>slint</c> section of <c>workspace/configuration</c>.</summary>
    public JsonObject ToWorkspaceConfiguration()
    {
        var includePaths = new JsonArray();
        foreach (var path in IncludePaths.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            includePaths.Add(path.Trim());
        }

        var libraryPaths = new JsonObject();
        foreach (var (name, path) in ParseLibraryPaths(LibraryPaths))
        {
            libraryPaths[name] = path;
        }

        return new JsonObject
        {
            ["includePaths"] = includePaths,
            ["libraryPaths"] = libraryPaths,
            ["preview"] = new JsonObject
            {
                ["style"] = PreviewStyle.Trim(),
                ["hide_ui"] = PreviewHideUi,
            },
        };
    }

    public static IReadOnlyDictionary<string, string> ParseLibraryPaths(IEnumerable<string> entries)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var separator = entry.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var name = entry[..separator].Trim();
            var path = entry[(separator + 1)..].Trim();
            if (path.Length > 0 && LibraryName().IsMatch(name))
            {
                result[name] = path;
            }
        }

        return result;
    }

    [GeneratedRegex("^[a-zA-Z][a-zA-Z0-9-_]*$")]
    private static partial Regex LibraryName();
}
