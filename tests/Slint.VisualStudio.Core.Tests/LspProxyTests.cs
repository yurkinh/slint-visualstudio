using System.Text.Json.Nodes;
using Nerdbank.Streams;
using Slint.VisualStudio.Core.Lsp;

namespace Slint.VisualStudio.Core.Tests;

public sealed class LspProxyTests : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly LspMessageReader vsReader;
    private readonly LspMessageWriter vsWriter;
    private readonly LspMessageReader serverReader;
    private readonly LspMessageWriter serverWriter;
    private readonly Stream vsSide;
    private readonly Stream serverSide;
    private readonly Stream proxyClientSide;
    private readonly Stream proxyServerSide;
    private readonly CancellationTokenSource stop = new();
    private Task? running;

    public LspProxyTests()
    {
        (vsSide, proxyClientSide) = FullDuplexStream.CreatePair();
        (serverSide, proxyServerSide) = FullDuplexStream.CreatePair();
        vsReader = new LspMessageReader(vsSide);
        vsWriter = new LspMessageWriter(vsSide);
        serverReader = new LspMessageReader(serverSide);
        serverWriter = new LspMessageWriter(serverSide);
    }

    private LspProxy Start(
        Func<ShowDocumentRequest, CancellationToken, Task<bool>>? showDocument = null,
        Func<string, bool>? shouldSync = null)
    {
        var proxy = new LspProxy(proxyClientSide, proxyServerSide, proxyServerSide)
        {
            ConfigurationProvider = () => new SlintConfiguration { IncludePaths = ["ui"] }.ToWorkspaceConfiguration(),
            ShowDocumentHandler = showDocument,
            ShouldSyncDocument = shouldSync ?? (_ => true),
        };
        running = proxy.RunAsync(stop.Token);
        return proxy;
    }

    [Fact]
    public async Task InitializeAdvertisesConfigurationAndShowDocumentSupport()
    {
        Start(showDocument: (_, _) => Task.FromResult(true));

        await vsWriter.WriteAsync(Request(1, "initialize", new JsonObject { ["capabilities"] = new JsonObject() }), default);
        var forwarded = await ReadAsync(serverReader);

        Assert.True((bool)forwarded["params"]!["capabilities"]!["workspace"]!["configuration"]!);
        Assert.True((bool)forwarded["params"]!["capabilities"]!["window"]!["showDocument"]!["support"]!);
    }

    [Fact]
    public async Task AnswersWorkspaceConfigurationForSlintSection()
    {
        Start();

        await serverWriter.WriteAsync(Request(7, "workspace/configuration", new JsonObject
        {
            ["items"] = new JsonArray(new JsonObject { ["section"] = "slint" }, new JsonObject { ["section"] = "other" }),
        }), default);
        var response = await ReadAsync(serverReader);

        Assert.Equal(7, (int)response["id"]!);
        var result = response["result"]!.AsArray();
        Assert.Equal("ui", (string?)result[0]!["includePaths"]![0]);
        Assert.Null(result[1]);
    }

    [Fact]
    public async Task ForwardsOtherServerMessagesToVisualStudio()
    {
        Start();

        await serverWriter.WriteAsync(Notification("textDocument/publishDiagnostics", new JsonObject { ["uri"] = "file:///a.slint" }), default);
        var forwarded = await ReadAsync(vsReader);

        Assert.Equal("textDocument/publishDiagnostics", (string?)forwarded["method"]);
    }

    [Fact]
    public async Task RaisesAndSwallowsTelemetryEvents()
    {
        var proxy = Start();
        var received = new TaskCompletionSource<JsonNode?>();
        proxy.TelemetryEventReceived += e => received.TrySetResult(e);

        await serverWriter.WriteAsync(Notification("telemetry/event", new JsonObject { ["type"] = "preview_opened" }), default);
        await serverWriter.WriteAsync(Notification("marker", null), default);

        Assert.Equal("preview_opened", (string?)(await received.Task.WaitAsync(Timeout))!["type"]);
        Assert.Equal("marker", (string?)(await ReadAsync(vsReader))["method"]);
    }

    [Fact]
    public async Task InjectedRequestsAreAnsweredWithoutReachingVisualStudio()
    {
        var proxy = Start();

        var pending = proxy.SendRequestAsync("workspace/executeCommand", new JsonObject { ["command"] = "slint/showPreview" }, default);
        var request = await ReadAsync(serverReader);
        await serverWriter.WriteAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = request["id"]!.DeepClone(), ["result"] = 42 }, default);
        await serverWriter.WriteAsync(Notification("marker", null), default);

        Assert.StartsWith("slint-vs-", (string?)request["id"]);
        Assert.Equal(42, (int)(await pending.WaitAsync(Timeout))!);
        Assert.Equal("marker", (string?)(await ReadAsync(vsReader))["method"]);
    }

    [Fact]
    public async Task InjectedRequestErrorsAreSurfaced()
    {
        var proxy = Start();

        var pending = proxy.SendRequestAsync("slint/unknown", null, default);
        var request = await ReadAsync(serverReader);
        await serverWriter.WriteAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = request["id"]!.DeepClone(),
            ["error"] = new JsonObject { ["code"] = -32601, ["message"] = "nope" },
        }, default);

        await Assert.ThrowsAsync<LspRequestException>(() => pending.WaitAsync(Timeout));
    }

    [Fact]
    public async Task DropsDocumentSyncForFilteredDocuments()
    {
        Start(shouldSync: uri => !SlintDocuments.IsRust(uri));

        await vsWriter.WriteAsync(Notification("textDocument/didOpen", TextDocument("file:///main.rs")), default);
        await vsWriter.WriteAsync(Notification("textDocument/didOpen", TextDocument("file:///app.slint")), default);
        var forwarded = await ReadAsync(serverReader);

        Assert.Equal("file:///app.slint", (string?)forwarded["params"]!["textDocument"]!["uri"]);
    }

    [Fact]
    public async Task HandlesShowDocumentWhenVisualStudioLacksSupport()
    {
        var shown = new TaskCompletionSource<ShowDocumentRequest>();
        Start(showDocument: (request, _) =>
        {
            shown.TrySetResult(request);
            return Task.FromResult(true);
        });
        await vsWriter.WriteAsync(Request(1, "initialize", new JsonObject()), default);
        await ReadAsync(serverReader);

        await serverWriter.WriteAsync(Request(3, "window/showDocument", new JsonObject { ["uri"] = "file:///app.slint", ["takeFocus"] = true }), default);
        var response = await ReadAsync(serverReader);

        Assert.Equal("file:///app.slint", (await shown.Task.WaitAsync(Timeout)).Uri);
        Assert.True((bool)response["result"]!["success"]!);
    }

    [Fact]
    public async Task ForwardsShowDocumentWhenVisualStudioSupportsIt()
    {
        Start(showDocument: (_, _) => throw new InvalidOperationException("must not be called"));
        var capabilities = new JsonObject
        {
            ["window"] = new JsonObject { ["showDocument"] = new JsonObject { ["support"] = true } },
        };
        await vsWriter.WriteAsync(Request(1, "initialize", new JsonObject { ["capabilities"] = capabilities }), default);
        await ReadAsync(serverReader);

        await serverWriter.WriteAsync(Request(3, "window/showDocument", new JsonObject { ["uri"] = "file:///app.slint" }), default);

        Assert.Equal("window/showDocument", (string?)(await ReadAsync(vsReader))["method"]);
    }

    [Fact]
    public async Task TracksShutdownAndCompletesWhenServerCloses()
    {
        var proxy = Start();

        await vsWriter.WriteAsync(Request(9, "shutdown", null), default);
        await ReadAsync(serverReader);
        serverSide.Dispose();

        await running!.WaitAsync(Timeout);
        Assert.True(proxy.ShutdownRequested);
    }

    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        vsSide.Dispose();
        serverSide.Dispose();
        if (running is not null)
        {
            try
            {
                await running.WaitAsync(Timeout);
            }
            catch (Exception) when (running.IsCanceled || running.IsFaulted)
            {
            }
        }
    }

    private static async Task<JsonObject> ReadAsync(LspMessageReader reader)
    {
        var body = await reader.ReadAsync(default).WaitAsync(Timeout);
        return JsonNode.Parse(body!)!.AsObject();
    }

    private static JsonObject Request(int id, string method, JsonNode? parameters) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters };

    private static JsonObject Notification(string method, JsonNode? parameters) =>
        new() { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = parameters };

    private static JsonObject TextDocument(string uri) =>
        new() { ["textDocument"] = new JsonObject { ["uri"] = uri } };
}
