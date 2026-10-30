using Slint.VisualStudio.Core.Server;

namespace Slint.VisualStudio.Core.Tests;

public class SlintConfigurationTests
{
    [Fact]
    public void BuildsWorkspaceConfigurationInSlintLspShape()
    {
        var configuration = new SlintConfiguration
        {
            IncludePaths = ["ui", " ", "shared "],
            LibraryPaths = ["widgets=libs/widgets", "1bad=x", "noequals", "empty="],
            PreviewStyle = " material ",
            PreviewHideUi = true,
        };

        var json = configuration.ToWorkspaceConfiguration();

        Assert.Equal(["ui", "shared"], json["includePaths"]!.AsArray().Select(n => (string)n!));
        Assert.Single(json["libraryPaths"]!.AsObject());
        Assert.Equal("libs/widgets", (string?)json["libraryPaths"]!["widgets"]);
        Assert.Equal("material", (string?)json["preview"]!["style"]);
        Assert.True((bool)json["preview"]!["hide_ui"]!);
    }

    [Theory]
    [InlineData("debug", "debug")]
    [InlineData(" TRACE ", "trace")]
    [InlineData("verbose", "info")]
    [InlineData(null, "info")]
    public void NormalizesLogLevel(string? input, string expected)
    {
        Assert.Equal(expected, ServerEnvironment.NormalizeLogLevel(input));
    }

    [Fact]
    public void EnvironmentContainsLogLevelAndPanicDirectory()
    {
        var environment = ServerEnvironment.Build("debug", "/tmp/panics");

        Assert.Equal("slint_lsp=debug", environment["RUST_LOG"]);
        Assert.Equal("/tmp/panics", environment[ServerEnvironment.PanicLogDirectoryVariable]);
    }

    [Theory]
    [InlineData("file:///app.slint", "", false, true)]
    [InlineData("file:///slint-project.json", "", false, true)]
    [InlineData("file:///main.rs", "slint::slint!{ }", true, true)]
    [InlineData("file:///main.rs", "slint::slint!{ }", false, false)]
    [InlineData("file:///main.rs", "fn main() {}", true, false)]
    public void DecidesWhetherDocumentWarrantsServer(string uri, string text, bool attachToRust, bool expected)
    {
        Assert.Equal(expected, SlintDocuments.WarrantsServer(uri, () => text, attachToRust));
    }
}
