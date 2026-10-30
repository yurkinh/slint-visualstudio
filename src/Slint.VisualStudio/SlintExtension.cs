using Microsoft.Extensions.DependencyInjection;

namespace Slint.VisualStudio;

[VisualStudioContribution]
internal sealed class SlintExtension : Extension
{
    public override ExtensionConfiguration ExtensionConfiguration => new()
    {
        Metadata = new(
            id: "Slint.VisualStudio.4b0e5d2c-6f0e-4d7b-9a51-2f6a3c8e1d47",
            version: this.ExtensionAssemblyVersion,
            publisherName: "Slint Community",
            displayName: "Slint",
            description: "Slint language support: diagnostics, completion, go-to definition, rename, and a live preview of Slint GUIs.")
        {
            InstallationTargetVersion = "[18.0,)",
        },
    };

    protected override void InitializeServices(IServiceCollection serviceCollection)
    {
        base.InitializeServices(serviceCollection);
        serviceCollection.AddSingleton<SlintSession>();
    }
}
