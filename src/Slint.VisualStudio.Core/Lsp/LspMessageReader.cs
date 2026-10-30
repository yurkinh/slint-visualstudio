using System.Globalization;
using System.Text;

namespace Slint.VisualStudio.Core.Lsp;

/// <summary>Reads LSP base-protocol messages (<c>Content-Length</c> framed JSON bodies).</summary>
public sealed class LspMessageReader
{
    private readonly Stream stream;
    private readonly byte[] single = new byte[1];

    public LspMessageReader(Stream stream)
    {
        this.stream = new BufferedStream(stream);
    }

    /// <returns>The message body, or <see langword="null"/> when the stream ended between messages.</returns>
    public async Task<byte[]?> ReadAsync(CancellationToken cancellationToken)
    {
        int? contentLength = null;
        var sawHeader = false;

        while (true)
        {
            var line = await ReadHeaderLineAsync(allowEndOfStream: !sawHeader, cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                return null;
            }

            if (line.Length == 0)
            {
                if (!sawHeader)
                {
                    continue;
                }

                break;
            }

            sawHeader = true;
            var separator = line.IndexOf(':');
            if (separator > 0 && line.AsSpan(0, separator).Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                contentLength = int.Parse(line.AsSpan(separator + 1).Trim(), NumberStyles.None, CultureInfo.InvariantCulture);
            }
        }

        if (contentLength is not int length)
        {
            throw new InvalidDataException("LSP message is missing the Content-Length header.");
        }

        var body = new byte[length];
        await stream.ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false);
        return body;
    }

    private async Task<string?> ReadHeaderLineAsync(bool allowEndOfStream, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        while (true)
        {
            if (await stream.ReadAsync(single, cancellationToken).ConfigureAwait(false) == 0)
            {
                if (allowEndOfStream && builder.Length == 0)
                {
                    return null;
                }

                throw new EndOfStreamException("LSP stream ended inside a message header.");
            }

            var c = (char)single[0];
            if (c == '\n')
            {
                return builder.ToString().TrimEnd('\r');
            }

            builder.Append(c);
        }
    }
}
