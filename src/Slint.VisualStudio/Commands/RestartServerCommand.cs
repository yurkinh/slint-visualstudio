using Microsoft.VisualStudio.Extensibility.Commands;

namespace Slint.VisualStudio.Commands;

[VisualStudioContribution]
internal sealed class RestartServerCommand : Command
{
    private readonly SlintSession session;

    public RestartServerCommand(VisualStudioExtensibility extensibility, SlintSession session)
        : base(extensibility)
    {
        this.session = session;
    }

    public override CommandConfiguration CommandConfiguration => new("Restart Slint Language Server");

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        await session.EnsureInitializedAsync(Extensibility, cancellationToken);
        await session.LogAsync("Restarting the Slint language server.");
        session.Restart();
    }
}
