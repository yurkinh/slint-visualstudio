using System.Text.Json.Nodes;

namespace Slint.VisualStudio.Core.Lsp;

public sealed record ShowDocumentRequest(string Uri, bool External, bool TakeFocus, JsonNode? Selection);

public sealed class LspRequestException(string message) : Exception(message);
