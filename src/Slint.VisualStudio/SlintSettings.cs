using Microsoft.VisualStudio.Extensibility.Settings;

namespace Slint.VisualStudio;

internal static class SlintSettings
{
    [VisualStudioContribution]
    internal static SettingCategory Category { get; } = new("slint", "Slint");

    [VisualStudioContribution]
    internal static Setting.String LspBinaryPath { get; } = new("lspBinaryPath", "Language server path", Category, defaultValue: "")
    {
        Description = "Path to slint-lsp. Leave empty to use the bundled server. A relative path is looked up on PATH. Restart the server to apply.",
    };

    [VisualStudioContribution]
    internal static Setting.StringArray LspArgs { get; } = new("lspArgs", "Language server arguments", Category, defaultValue: new string[] { })
    {
        Description = "Command line arguments passed to slint-lsp. Restart the server to apply.",
    };

    [VisualStudioContribution]
    internal static Setting.Enum LogLevel { get; } = new(
        "logLevel",
        "Language server log level",
        Category,
        [
            new("info", "Info"),
            new("debug", "Debug"),
            new("trace", "Trace"),
        ],
        defaultValue: "info")
    {
        Description = "Log level of slint-lsp, written to the Slint output pane. Restart the server to apply.",
    };

    [VisualStudioContribution]
    internal static Setting.StringArray IncludePaths { get; } = new("includePaths", "Include paths", Category, defaultValue: new string[] { })
    {
        Description = "Paths in which `import` statements and `@image-url` are looked up.",
    };

    [VisualStudioContribution]
    internal static Setting.StringArray LibraryPaths { get; } = new("libraryPaths", "Library paths", Category, defaultValue: new string[] { })
    {
        Description = "Library locations for `@mylibrary` imports, one `name=path` entry per line.",
    };

    [VisualStudioContribution]
    internal static Setting.String PreviewStyle { get; } = new("previewStyle", "Preview style", Category, defaultValue: "")
    {
        Description = "Default widget style for the preview, for example 'fluent', 'material', 'cupertino', or 'native'.",
    };

    [VisualStudioContribution]
    internal static Setting.Boolean PreviewHideUi { get; } = new("previewHideUi", "Hide preview toolbar", Category, defaultValue: false)
    {
        Description = "Hide the toolbar of the live preview.",
    };

    [VisualStudioContribution]
    internal static Setting.Boolean AttachToRust { get; } = new("attachToRust", "Use in Rust files", Category, defaultValue: true)
    {
        Description = "Start the Slint language server for Rust files that contain the slint! macro.",
    };

    public static async Task<SlintConfiguration> ReadAsync(VisualStudioExtensibility extensibility, CancellationToken cancellationToken)
    {
        var settings = extensibility.Settings();
        return new SlintConfiguration
        {
            LspBinaryPath = (await settings.ReadEffectiveValueAsync(LspBinaryPath, cancellationToken)).ValueOrDefault(defaultValue: ""),
            LspArgs = (await settings.ReadEffectiveValueAsync(LspArgs, cancellationToken)).ValueOrDefault(defaultValue: []),
            LogLevel = (await settings.ReadEffectiveValueAsync(LogLevel, cancellationToken)).ValueOrDefault(defaultValue: "info"),
            IncludePaths = (await settings.ReadEffectiveValueAsync(IncludePaths, cancellationToken)).ValueOrDefault(defaultValue: []),
            LibraryPaths = (await settings.ReadEffectiveValueAsync(LibraryPaths, cancellationToken)).ValueOrDefault(defaultValue: []),
            PreviewStyle = (await settings.ReadEffectiveValueAsync(PreviewStyle, cancellationToken)).ValueOrDefault(defaultValue: ""),
            PreviewHideUi = (await settings.ReadEffectiveValueAsync(PreviewHideUi, cancellationToken)).ValueOrDefault(defaultValue: false),
            AttachToRust = (await settings.ReadEffectiveValueAsync(AttachToRust, cancellationToken)).ValueOrDefault(defaultValue: true),
        };
    }

    public static async Task<IReadOnlyList<IDisposable>> SubscribeAsync(
        VisualStudioExtensibility extensibility,
        Action onChanged,
        CancellationToken cancellationToken)
    {
        var settings = extensibility.Settings();
        return
        [
            await settings.SubscribeAsync(LspBinaryPath, cancellationToken, _ => onChanged()),
            await settings.SubscribeAsync(LspArgs, cancellationToken, _ => onChanged()),
            await settings.SubscribeAsync(LogLevel, cancellationToken, _ => onChanged()),
            await settings.SubscribeAsync(IncludePaths, cancellationToken, _ => onChanged()),
            await settings.SubscribeAsync(LibraryPaths, cancellationToken, _ => onChanged()),
            await settings.SubscribeAsync(PreviewStyle, cancellationToken, _ => onChanged()),
            await settings.SubscribeAsync(PreviewHideUi, cancellationToken, _ => onChanged()),
            await settings.SubscribeAsync(AttachToRust, cancellationToken, _ => onChanged()),
        ];
    }
}
