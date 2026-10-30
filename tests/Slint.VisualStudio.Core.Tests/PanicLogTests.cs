using Slint.VisualStudio.Core.Server;

namespace Slint.VisualStudio.Core.Tests;

public sealed class PanicLogTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "slint-panic-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ParsesMultilineMessage()
    {
        var log = PanicLog.Parse("slint-lsp v1.18.1\nsrc/main.rs:1:2\nbacktrace\nfirst\r\nsecond\n");

        Assert.Equal(new PanicLog("slint-lsp v1.18.1", "src/main.rs:1:2", "backtrace", "first\nsecond"), log);
    }

    [Fact]
    public void RejectsTruncatedLog()
    {
        Assert.Null(PanicLog.Parse("slint-lsp v1.18.1\nlocation"));
    }

    [Fact]
    public void TakesNewestAndDeletesAll()
    {
        Directory.CreateDirectory(directory);
        var older = Path.Combine(directory, "slint_lsp_panic_1.log");
        var newer = Path.Combine(directory, "slint_lsp_panic_2.log");
        File.WriteAllText(older, "v\nloc\nbt\nold");
        File.WriteAllText(newer, "v\nloc\nbt\nnew");
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddMinutes(-5));

        var log = PanicLog.TakeLatest(directory);

        Assert.Equal("new", log?.Message);
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public void ReturnsNullForMissingDirectory()
    {
        Assert.Null(PanicLog.TakeLatest(directory));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
