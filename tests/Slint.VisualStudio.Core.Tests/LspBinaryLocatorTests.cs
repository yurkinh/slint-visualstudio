using System.Runtime.InteropServices;
using Slint.VisualStudio.Core.Server;

namespace Slint.VisualStudio.Core.Tests;

public class LspBinaryLocatorTests
{
    private static readonly string ExtensionDirectory = Path.Combine(Path.GetTempPath(), "ext");
    private static readonly string Bin = Path.Combine(ExtensionDirectory, "bin");

    [Theory]
    [InlineData(Architecture.X64, "slint-lsp-x86_64-pc-windows-msvc.exe")]
    [InlineData(Architecture.Arm64, "slint-lsp-aarch64-pc-windows-msvc.exe")]
    public void PicksBundledBinaryForArchitecture(Architecture architecture, string expected)
    {
        var path = Path.Combine(Bin, expected);

        var found = LspBinaryLocator.Find(ExtensionDirectory, "", architecture, null, p => p == path);

        Assert.Equal(path, found);
    }

    [Fact]
    public void PrefersPlainBundledBinaryForLocalBuilds()
    {
        var plain = Path.Combine(Bin, "slint-lsp.exe");

        var found = LspBinaryLocator.Find(ExtensionDirectory, null, Architecture.X64, null, _ => true);

        Assert.Equal(plain, found);
    }

    [Fact]
    public void UsesAbsoluteOverrideOnly()
    {
        var custom = Path.Combine(Path.GetTempPath(), "custom", "slint-lsp.exe");

        Assert.Equal(custom, LspBinaryLocator.Find(ExtensionDirectory, custom, Architecture.X64, null, p => p == custom));
        Assert.Null(LspBinaryLocator.Find(ExtensionDirectory, custom, Architecture.X64, null, p => p != custom));
    }

    [Fact]
    public void ResolvesRelativeOverrideOnPath()
    {
        var first = Path.Combine(Path.GetTempPath(), "one");
        var second = Path.Combine(Path.GetTempPath(), "two");
        var expected = Path.Combine(second, "slint-lsp.exe");

        var found = LspBinaryLocator.Find(
            ExtensionDirectory,
            "slint-lsp",
            Architecture.X64,
            first + Path.PathSeparator + second,
            p => p == expected);

        Assert.Equal(expected, found);
    }

    [Fact]
    public void ReturnsNullWhenNothingExists()
    {
        Assert.Null(LspBinaryLocator.Find(ExtensionDirectory, "", Architecture.X64, null, _ => false));
        Assert.Null(LspBinaryLocator.Find(ExtensionDirectory, "slint-lsp", Architecture.X64, null, _ => true));
    }
}
