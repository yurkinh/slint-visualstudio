using System.Text;
using System.Text.Json.Nodes;
using Nerdbank.Streams;
using Slint.VisualStudio.Core.Lsp;

namespace Slint.VisualStudio.Core.Tests;

public class LspMessageFramingTests
{
    [Fact]
    public async Task RoundTripsMultipleMessages()
    {
        var (a, b) = FullDuplexStream.CreatePair();
        var writer = new LspMessageWriter(a);
        var reader = new LspMessageReader(b);

        await writer.WriteAsync(new JsonObject { ["method"] = "one", ["text"] = "▶ Show Preview" }, default);
        await writer.WriteAsync(new JsonObject { ["method"] = "two" }, default);

        var first = JsonNode.Parse((await reader.ReadAsync(default))!)!;
        var second = JsonNode.Parse((await reader.ReadAsync(default))!)!;

        Assert.Equal("one", (string?)first["method"]);
        Assert.Equal("▶ Show Preview", (string?)first["text"]);
        Assert.Equal("two", (string?)second["method"]);
    }

    [Fact]
    public async Task AcceptsAdditionalHeaders()
    {
        var body = "{\"id\":1}";
        var raw = $"Content-Type: application/vscode-jsonrpc; charset=utf-8\r\ncontent-length: {body.Length}\r\n\r\n{body}";
        var reader = new LspMessageReader(new MemoryStream(Encoding.ASCII.GetBytes(raw)));

        var message = await reader.ReadAsync(default);

        Assert.Equal(body, Encoding.UTF8.GetString(message!));
    }

    [Fact]
    public async Task ReturnsNullAtEndOfStream()
    {
        var reader = new LspMessageReader(new MemoryStream());

        Assert.Null(await reader.ReadAsync(default));
    }

    [Fact]
    public async Task ThrowsWhenContentLengthIsMissing()
    {
        var reader = new LspMessageReader(new MemoryStream(Encoding.ASCII.GetBytes("Content-Type: x\r\n\r\n{}")));

        await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadAsync(default));
    }

    [Fact]
    public async Task ThrowsWhenStreamEndsInsideHeader()
    {
        var reader = new LspMessageReader(new MemoryStream(Encoding.ASCII.GetBytes("Content-Len")));

        await Assert.ThrowsAsync<EndOfStreamException>(() => reader.ReadAsync(default));
    }
}
