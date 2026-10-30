using System.Text.Json.Nodes;
using Microsoft.VisualStudio.Extensibility.Documents;
using Microsoft.VisualStudio.Extensibility.Shell;
using Slint.VisualStudio.Core.Lsp;

namespace Slint.VisualStudio;

/// <summary>Shared state between the language server provider, document listener, and commands.</summary>
internal sealed class SlintSession
{
    private readonly SemaphoreSlim initializeGate = new(1, 1);
    private readonly SemaphoreSlim logGate = new(1, 1);
    private readonly List<IDisposable> settingsSubscriptions = [];
    private readonly TraceSource traceSource;
    private VisualStudioExtensibility? extensibility;
    private OutputChannel? output;
    private TaskCompletionSource<LspProxy> proxySource = NewProxySource();
    private volatile LspProxy? currentProxy;
    private volatile bool initialized;

    public SlintSession(TraceSource traceSource)
    {
        this.traceSource = traceSource;
    }

    /// <summary>Raised when the language server provider should be enabled or disabled.</summary>
    public event Action<bool>? EnabledChanged;

    public bool IsEnabled { get; private set; }

    public SlintConfiguration Configuration { get; private set; } = new();

    public async Task EnsureInitializedAsync(VisualStudioExtensibility extensibility, CancellationToken cancellationToken)
    {
        if (initialized)
        {
            return;
        }

        await initializeGate.WaitAsync(cancellationToken);
        try
        {
            if (initialized)
            {
                return;
            }

            this.extensibility = extensibility;
            output = await extensibility.Views().Output.CreateOutputChannelAsync("Slint", cancellationToken);
            Configuration = await SlintSettings.ReadAsync(extensibility, cancellationToken);
            settingsSubscriptions.AddRange(await SlintSettings.SubscribeAsync(extensibility, OnSettingsChanged, cancellationToken));
            initialized = true;
        }
        finally
        {
            initializeGate.Release();
        }
    }

    public void Enable()
    {
        if (IsEnabled)
        {
            return;
        }

        IsEnabled = true;
        EnabledChanged?.Invoke(true);
    }

    public void Restart()
    {
        if (!IsEnabled)
        {
            Enable();
            return;
        }

        EnabledChanged?.Invoke(false);
        EnabledChanged?.Invoke(true);
    }

    public void Attach(LspProxy proxy)
    {
        currentProxy = proxy;
        proxySource.TrySetResult(proxy);
    }

    public void Detach(LspProxy proxy)
    {
        if (ReferenceEquals(currentProxy, proxy))
        {
            currentProxy = null;
            proxySource = NewProxySource();
        }
    }

    /// <returns><see langword="false"/> when no server became available in time.</returns>
    public async Task<bool> ShowPreviewAsync(Uri document, string component, CancellationToken cancellationToken)
    {
        Enable();

        LspProxy proxy;
        try
        {
            proxy = await proxySource.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        }
        catch (TimeoutException)
        {
            return false;
        }

        // slint-lsp accepts slint/showPreview as a notification, which avoids routing through VS's client.
        await proxy.SendNotificationAsync("slint/showPreview", new JsonArray(document.AbsoluteUri, component), cancellationToken);
        return true;
    }

    public async Task LogAsync(string line)
    {
        if (output is null)
        {
            traceSource.TraceInformation(line);
            return;
        }

        await logGate.WaitAsync();
        try
        {
            await output.Writer.WriteLineAsync(line);
        }
        catch (Exception ex)
        {
            traceSource.TraceEvent(TraceEventType.Warning, 0, "Failed to write to the Slint output pane: {0}", ex.Message);
        }
        finally
        {
            logGate.Release();
        }
    }

    public async Task ShowErrorAsync(string message)
    {
        if (extensibility is null)
        {
            return;
        }

        try
        {
            await extensibility.Shell().ShowPromptAsync(message, PromptOptions.OK, CancellationToken.None);
        }
        catch (Exception ex)
        {
            traceSource.TraceEvent(TraceEventType.Warning, 0, "Failed to show prompt: {0}", ex.Message);
        }
    }

    private void OnSettingsChanged()
    {
        // Subscriptions fire once with the current value while initializing; that value was already read.
        if (initialized)
        {
            _ = ReloadSettingsAsync();
        }
    }

    private async Task ReloadSettingsAsync()
    {
        try
        {
            Configuration = await SlintSettings.ReadAsync(extensibility!, CancellationToken.None);
            if (currentProxy is { } proxy)
            {
                await proxy.SendNotificationAsync(
                    "workspace/didChangeConfiguration",
                    new JsonObject { ["settings"] = "" },
                    CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            await LogAsync("Failed to apply Slint settings: " + ex.Message);
        }
    }

    private static TaskCompletionSource<LspProxy> NewProxySource() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
