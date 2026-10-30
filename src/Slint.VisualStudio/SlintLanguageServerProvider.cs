using System.IO.Pipelines;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Extensibility.Documents;
using Microsoft.VisualStudio.Extensibility.Editor;
using Microsoft.VisualStudio.Extensibility.LanguageServer;
using Microsoft.VisualStudio.RpcContracts.LanguageServerProvider;
using Nerdbank.Streams;
using Slint.VisualStudio.Core.Lsp;
using Slint.VisualStudio.Core.Server;

namespace Slint.VisualStudio;

[VisualStudioContribution]
internal sealed class SlintLanguageServerProvider : LanguageServerProvider
{
    private static readonly string PanicLogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Slint.VisualStudio",
        "slint-lsp-panics");

    private readonly SlintSession session;

    public SlintLanguageServerProvider(ExtensionCore container, VisualStudioExtensibility extensibility, SlintSession session)
        : base(container, extensibility)
    {
        this.session = session;
        Enabled = session.IsEnabled;
        session.EnabledChanged += enabled => Enabled = enabled;
    }

    [VisualStudioContribution]
    internal static DocumentTypeConfiguration SlintDocumentType => new("slint")
    {
        FileExtensions = new[] { ".slint", ".60" },
        BaseDocumentType = LanguageServerBaseDocumentType,
    };

    [VisualStudioContribution]
    internal static DocumentTypeConfiguration RustDocumentType => new("rust")
    {
        FileExtensions = new[] { ".rs" },
        BaseDocumentType = LanguageServerBaseDocumentType,
    };

    public override LanguageServerProviderConfiguration LanguageServerProviderConfiguration => new(
        "Slint",
        new[]
        {
            DocumentFilter.FromDocumentType(SlintDocumentType),
            DocumentFilter.FromDocumentType(RustDocumentType),
        });

    public override async Task<IDuplexPipe?> CreateServerConnectionAsync(CancellationToken cancellationToken)
    {
        await session.EnsureInitializedAsync(Extensibility, cancellationToken);
        var configuration = session.Configuration;

        var extensionDirectory = Path.GetDirectoryName(typeof(SlintLanguageServerProvider).Assembly.Location)!;
        var binary = LspBinaryLocator.Find(
            extensionDirectory,
            configuration.LspBinaryPath,
            RuntimeInformation.OSArchitecture,
            Environment.GetEnvironmentVariable("PATH"));

        if (binary is null)
        {
            await session.LogAsync(string.IsNullOrWhiteSpace(configuration.LspBinaryPath)
                ? "Could not find the bundled slint-lsp binary."
                : $"Could not find slint-lsp at '{configuration.LspBinaryPath}'. Check Tools > Options > Slint.");
            return null;
        }

        Directory.CreateDirectory(PanicLogDirectory);
        PanicLog.TakeLatest(PanicLogDirectory);

        SlintServerProcess server;
        try
        {
            server = SlintServerProcess.Start(
                binary,
                configuration.LspArgs,
                ServerEnvironment.Build(configuration.LogLevel, PanicLogDirectory),
                line => _ = session.LogAsync(line));
        }
        catch (Exception ex)
        {
            await session.LogAsync($"Failed to start '{binary}': {ex.Message}");
            return null;
        }

        await session.LogAsync($"Started {binary}");

        var (visualStudioSide, proxySide) = FullDuplexStream.CreatePair();
        var proxy = new LspProxy(proxySide, server.StandardOutput, server.StandardInput)
        {
            ConfigurationProvider = () => session.Configuration.ToWorkspaceConfiguration(),
            ShouldSyncDocument = uri => !SlintDocuments.IsRust(uri) || session.Configuration.AttachToRust,
            ShowDocumentHandler = ShowDocumentAsync,
        };

        session.Attach(proxy);
        _ = RunServerAsync(server, proxy, proxySide);

        return new DuplexPipe(visualStudioSide.UsePipeReader(), visualStudioSide.UsePipeWriter());
    }

    public override Task OnServerInitializationResultAsync(
        ServerInitializationResult serverInitializationResult,
        LanguageServerInitializationFailureInfo? initializationFailureInfo,
        CancellationToken cancellationToken)
    {
        if (serverInitializationResult == ServerInitializationResult.Failed)
        {
            _ = session.LogAsync("Slint language server failed to initialize: " + initializationFailureInfo?.StatusMessage);
        }

        return base.OnServerInitializationResultAsync(serverInitializationResult, initializationFailureInfo, cancellationToken);
    }

    private async Task RunServerAsync(SlintServerProcess server, LspProxy proxy, Stream proxySide)
    {
        try
        {
            await proxy.RunAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await session.LogAsync("Slint language server connection failed: " + ex.Message);
        }
        finally
        {
            session.Detach(proxy);
            await server.DisposeAsync();
            await proxySide.DisposeAsync();
        }

        if (proxy.ShutdownRequested)
        {
            return;
        }

        var exitCode = server.ExitCode?.ToString() ?? "unknown";
        await session.LogAsync($"The Slint language server stopped unexpectedly (exit code {exitCode}).");
        if (PanicLog.TakeLatest(PanicLogDirectory) is { } panic)
        {
            await session.LogAsync($"{panic.Version} panicked at {panic.Location}:{Environment.NewLine}{panic.Message}");
        }

        await session.ShowErrorAsync(
            "The Slint language server crashed. See the Slint output pane for details, and please report it at https://github.com/slint-ui/slint/issues. Use Extensions > Slint > Restart Language Server to start it again.");
    }

    private async Task<bool> ShowDocumentAsync(ShowDocumentRequest request, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(request.Uri, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (request.External)
        {
            if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            {
                return false;
            }

            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
            return true;
        }

        if (!uri.IsFile)
        {
            return false;
        }

        await Extensibility.Documents().OpenDocumentAsync(uri, cancellationToken);
        return true;
    }
}
