using System.Runtime.InteropServices;

namespace Slint.VisualStudio.Core.Server;

public static class LspBinaryLocator
{
    public static string BundledFileName(Architecture architecture) => architecture switch
    {
        Architecture.Arm64 => "slint-lsp-aarch64-pc-windows-msvc.exe",
        _ => "slint-lsp-x86_64-pc-windows-msvc.exe",
    };

    /// <summary>
    /// Mirrors the VS Code extension: without an override use the bundled binary; an absolute override is
    /// used as-is; a relative override is looked up on PATH.
    /// </summary>
    public static string? Find(
        string extensionDirectory,
        string? configuredPath,
        Architecture architecture,
        string? pathEnvironment,
        Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;

        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            var bin = Path.Combine(extensionDirectory, "bin");
            return new[]
            {
                Path.Combine(bin, "slint-lsp.exe"),
                Path.Combine(bin, BundledFileName(architecture)),
            }.FirstOrDefault(fileExists);
        }

        configuredPath = configuredPath.Trim();
        if (Path.IsPathRooted(configuredPath))
        {
            return fileExists(configuredPath) ? configuredPath : null;
        }

        if (string.IsNullOrEmpty(pathEnvironment))
        {
            return null;
        }

        var names = Path.HasExtension(configuredPath) ? new[] { configuredPath } : new[] { configuredPath, configuredPath + ".exe" };
        return pathEnvironment
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(directory => names.Select(name => Path.Combine(directory, name)))
            .FirstOrDefault(fileExists);
    }
}
