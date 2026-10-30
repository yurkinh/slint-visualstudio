using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Slint.VisualStudio.Core.Lsp;

/// <summary>
/// Sits between Visual Studio's language client and slint-lsp. Forwards traffic unchanged except for
/// configuration requests, show-document requests VS cannot handle, telemetry, filtered document sync,
/// and requests injected by the extension.
/// </summary>
public sealed class LspProxy
{
    private const string InjectedIdPrefix = "slint-vs-";

    private static readonly HashSet<string> DocumentSyncMethods = new(StringComparer.Ordinal)
    {
        "textDocument/didOpen",
        "textDocument/didChange",
        "textDocument/didClose",
        "textDocument/didSave",
        "textDocument/willSave",
    };

    private readonly LspMessageReader clientReader;
    private readonly LspMessageWriter clientWriter;
    private readonly LspMessageReader serverReader;
    private readonly LspMessageWriter serverWriter;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonNode?>> pendingRequests = new(StringComparer.Ordinal);
    private int nextRequestId;
    private volatile bool interceptShowDocument;
    private volatile bool shutdownRequested;

    /// <param name="client">Duplex stream connected to Visual Studio.</param>
    /// <param name="serverOutput">The server's standard output.</param>
    /// <param name="serverInput">The server's standard input.</param>
    public LspProxy(Stream client, Stream serverOutput, Stream serverInput)
    {
        clientReader = new LspMessageReader(client);
        clientWriter = new LspMessageWriter(client);
        serverReader = new LspMessageReader(serverOutput);
        serverWriter = new LspMessageWriter(serverInput);
    }

    /// <summary>Returns the value of the <c>slint</c> configuration section.</summary>
    public Func<JsonObject> ConfigurationProvider { get; init; } = () => new JsonObject();

    /// <summary>Decides whether document sync notifications for a document URI reach the server.</summary>
    public Func<string, bool> ShouldSyncDocument { get; init; } = _ => true;

    /// <summary>Handles <c>window/showDocument</c> when Visual Studio does not advertise support for it.</summary>
    public Func<ShowDocumentRequest, CancellationToken, Task<bool>>? ShowDocumentHandler { get; init; }

    public event Action<JsonNode?>? TelemetryEventReceived;

    /// <summary>True once Visual Studio asked the server to shut down, so a subsequent exit is not a crash.</summary>
    public bool ShutdownRequested => shutdownRequested;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var finished = await Task.WhenAny(
            PumpClientToServerAsync(stop.Token),
            PumpServerToClientAsync(stop.Token)).ConfigureAwait(false);

        stop.Cancel();
        foreach (var pending in pendingRequests.Values)
        {
            pending.TrySetCanceled(CancellationToken.None);
        }

        pendingRequests.Clear();
        await finished.ConfigureAwait(false);
    }

    public Task SendNotificationAsync(string method, JsonNode? parameters, CancellationToken cancellationToken)
    {
        var message = new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method };
        if (parameters is not null)
        {
            message["params"] = parameters;
        }

        return serverWriter.WriteAsync(message, cancellationToken);
    }

    public async Task<JsonNode?> SendRequestAsync(string method, JsonNode? parameters, CancellationToken cancellationToken)
    {
        var id = InjectedIdPrefix + Interlocked.Increment(ref nextRequestId).ToString(CultureInfo.InvariantCulture);
        var completion = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingRequests[id] = completion;

        var message = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method };
        if (parameters is not null)
        {
            message["params"] = parameters;
        }

        using var registration = cancellationToken.Register(() =>
        {
            if (pendingRequests.TryRemove(id, out var pending))
            {
                pending.TrySetCanceled(cancellationToken);
            }
        });

        try
        {
            await serverWriter.WriteAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            pendingRequests.TryRemove(id, out _);
            throw;
        }

        return await completion.Task.ConfigureAwait(false);
    }

    private async Task PumpClientToServerAsync(CancellationToken cancellationToken)
    {
        while (await clientReader.ReadAsync(cancellationToken).ConfigureAwait(false) is { } body)
        {
            var message = TryParse(body);
            var method = GetString(message, "method");

            if (method is "shutdown" or "exit")
            {
                shutdownRequested = true;
            }
            else if (method == "initialize" && message?["params"] is JsonObject initializeParams)
            {
                PatchInitialize(initializeParams);
                await serverWriter.WriteAsync(message, cancellationToken).ConfigureAwait(false);
                continue;
            }
            else if (method is not null && DocumentSyncMethods.Contains(method) && !ShouldSync(message!))
            {
                continue;
            }

            await serverWriter.WriteRawAsync(body, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PumpServerToClientAsync(CancellationToken cancellationToken)
    {
        while (await serverReader.ReadAsync(cancellationToken).ConfigureAwait(false) is { } body)
        {
            var message = TryParse(body);
            if (message is not null && await TryHandleServerMessageAsync(message, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            await clientWriter.WriteRawAsync(body, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> TryHandleServerMessageAsync(JsonObject message, CancellationToken cancellationToken)
    {
        var method = GetString(message, "method");
        var id = message["id"];

        if (method is null)
        {
            if (GetString(message, "id") is { } responseId
                && responseId.StartsWith(InjectedIdPrefix, StringComparison.Ordinal)
                && pendingRequests.TryRemove(responseId, out var pending))
            {
                if (message["error"] is { } error)
                {
                    pending.TrySetException(new LspRequestException(error.ToJsonString()));
                }
                else
                {
                    pending.TrySetResult(message["result"]?.DeepClone());
                }

                return true;
            }

            return false;
        }

        switch (method)
        {
            case "workspace/configuration" when id is not null:
                await serverWriter.WriteAsync(CreateResponse(id, BuildConfigurationResult(message["params"])), cancellationToken).ConfigureAwait(false);
                return true;

            case "window/showDocument" when id is not null && interceptShowDocument && ShowDocumentHandler is not null:
                _ = HandleShowDocumentAsync(id.DeepClone(), message["params"]?.DeepClone(), cancellationToken);
                return true;

            case "telemetry/event" when id is null:
                TelemetryEventReceived?.Invoke(message["params"]?.DeepClone());
                return true;

            default:
                return false;
        }
    }

    private async Task HandleShowDocumentAsync(JsonNode id, JsonNode? parameters, CancellationToken cancellationToken)
    {
        var success = false;
        try
        {
            if (parameters is JsonObject p && GetString(p, "uri") is { } uri)
            {
                var request = new ShowDocumentRequest(
                    uri,
                    GetBool(p, "external"),
                    GetBool(p, "takeFocus"),
                    p["selection"]?.DeepClone());
                success = await ShowDocumentHandler!(request, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            success = false;
        }

        try
        {
            await serverWriter.WriteAsync(CreateResponse(id, new JsonObject { ["success"] = success }), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private JsonArray BuildConfigurationResult(JsonNode? parameters)
    {
        var result = new JsonArray();
        if (parameters is not JsonObject p || p["items"] is not JsonArray items)
        {
            return result;
        }

        foreach (var item in items)
        {
            var section = item is JsonObject itemObject ? GetString(itemObject, "section") : null;
            result.Add(section switch
            {
                "slint" => ConfigurationProvider(),
                null => new JsonObject { ["slint"] = ConfigurationProvider() },
                _ => null,
            });
        }

        return result;
    }

    private void PatchInitialize(JsonObject parameters)
    {
        var capabilities = GetOrAddObject(parameters, "capabilities");
        GetOrAddObject(capabilities, "workspace")["configuration"] = true;

        if (ShowDocumentHandler is not null)
        {
            var showDocument = GetOrAddObject(GetOrAddObject(capabilities, "window"), "showDocument");
            if (!GetBool(showDocument, "support"))
            {
                showDocument["support"] = true;
                interceptShowDocument = true;
            }
        }
    }

    private bool ShouldSync(JsonObject message) =>
        message["params"] is not JsonObject p
        || p["textDocument"] is not JsonObject textDocument
        || GetString(textDocument, "uri") is not { } uri
        || ShouldSyncDocument(uri);

    private static JsonObject CreateResponse(JsonNode id, JsonNode? result) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["result"] = result };

    private static JsonObject GetOrAddObject(JsonObject parent, string name)
    {
        if (parent[name] is JsonObject existing)
        {
            return existing;
        }

        var created = new JsonObject();
        parent[name] = created;
        return created;
    }

    private static string? GetString(JsonObject? node, string name) =>
        node?[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static bool GetBool(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;

    private static JsonObject? TryParse(byte[] body)
    {
        try
        {
            return JsonNode.Parse(body) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
