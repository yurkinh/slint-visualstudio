using Microsoft.VisualStudio.Extensibility.Commands;

namespace Slint.VisualStudio.Commands;

internal static class SlintMenu
{
    [VisualStudioContribution]
    public static MenuConfiguration Menu => new("Slint")
    {
        Placements = new[] { CommandPlacement.KnownPlacements.ExtensionsMenu },
        Children = new[]
        {
            MenuChild.Command<ShowPreviewCommand>(),
            MenuChild.Command<RestartServerCommand>(),
        },
    };
}
