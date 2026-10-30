using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Editor;
using Microsoft.VisualStudio.Extensibility.Shell;

namespace Slint.VisualStudio.Commands;

[VisualStudioContribution]
internal sealed class ShowPreviewCommand : Command
{
    private readonly SlintSession session;

    public ShowPreviewCommand(VisualStudioExtensibility extensibility, SlintSession session)
        : base(extensibility)
    {
        this.session = session;
    }

    public override CommandConfiguration CommandConfiguration => new("Show Slint Preview")
    {
        TooltipText = "Open a live preview of the current .slint file.",
        EnabledWhen = ActivationConstraint.ClientContext(ClientContextKey.Shell.ActiveEditorFileName, @"\.(slint|60)$"),
    };

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        var textView = await context.GetActiveTextViewAsync(cancellationToken);
        if (textView is null)
        {
            return;
        }

        await session.EnsureInitializedAsync(Extensibility, cancellationToken);
        if (!await session.ShowPreviewAsync(textView.Document.Uri, "", cancellationToken))
        {
            await Extensibility.Shell().ShowPromptAsync(
                "The Slint language server is not running. See the Slint output pane for details.",
                PromptOptions.OK,
                cancellationToken);
        }
    }
}
