using Microsoft.VisualStudio.Extensibility.Editor;

namespace Slint.VisualStudio;

/// <summary>Enables the language server once a document that uses Slint is opened or edited.</summary>
[VisualStudioContribution]
internal sealed class SlintDocumentListener : ExtensionPart, ITextViewOpenClosedListener, ITextViewChangedListener
{
    private readonly SlintSession session;

    public SlintDocumentListener(ExtensionCore container, VisualStudioExtensibility extensibility, SlintSession session)
        : base(container, extensibility)
    {
        this.session = session;
    }

    public TextViewExtensionConfiguration TextViewExtensionConfiguration => new()
    {
        AppliesTo = new[]
        {
            DocumentFilter.FromDocumentType(SlintLanguageServerProvider.SlintDocumentType),
            DocumentFilter.FromDocumentType(SlintLanguageServerProvider.RustDocumentType),
        },
    };

    public Task TextViewOpenedAsync(ITextViewSnapshot textView, CancellationToken cancellationToken) =>
        EvaluateAsync(textView, cancellationToken);

    public Task TextViewClosedAsync(ITextViewSnapshot textView, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task TextViewChangedAsync(TextViewChangedArgs args, CancellationToken cancellationToken) =>
        EvaluateAsync(args.AfterTextView, cancellationToken);

    private async Task EvaluateAsync(ITextViewSnapshot textView, CancellationToken cancellationToken)
    {
        if (session.IsEnabled)
        {
            return;
        }

        await session.EnsureInitializedAsync(Extensibility, cancellationToken);
        var document = textView.Document;
        if (SlintDocuments.WarrantsServer(document.Uri.AbsolutePath, () => document.Text.CopyToString(), session.Configuration.AttachToRust))
        {
            session.Enable();
        }
    }
}
