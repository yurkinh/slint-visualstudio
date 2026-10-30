using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Slint.VisualStudio.Core.Lsp;

/// <summary>Writes LSP base-protocol messages; safe to call concurrently.</summary>
public sealed class LspMessageWriter
{
    private readonly Stream stream;
    private readonly SemaphoreSlim gate = new(1, 1);

    public LspMessageWriter(Stream stream)
    {
        this.stream = stream;
    }

    public Task WriteAsync(JsonNode message, CancellationToken cancellationToken) =>
        WriteRawAsync(Encoding.UTF8.GetBytes(message.ToJsonString()), cancellationToken);

    public async Task WriteRawAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        var header = Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"Content-Length: {body.Length}\r\n\r\n"));

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }
}
