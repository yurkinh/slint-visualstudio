using System.Diagnostics;

namespace Slint.VisualStudio.Core.Server;

public sealed class SlintServerProcess : IAsyncDisposable
{
    private static readonly TimeSpan ExitGracePeriod = TimeSpan.FromSeconds(2);

    private readonly Process process;
    private readonly TaskCompletionSource<int> exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private SlintServerProcess(Process process)
    {
        this.process = process;
    }

    public Stream StandardInput => process.StandardInput.BaseStream;

    public Stream StandardOutput => process.StandardOutput.BaseStream;

    /// <summary>Completes with the exit code once the process has exited.</summary>
    public Task<int> Exited => exited.Task;

    public int? ExitCode => exited.Task.IsCompletedSuccessfully ? exited.Task.Result : null;

    public static SlintServerProcess Start(
        string fileName,
        IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        Action<string> onErrorLine)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in environment)
        {
            startInfo.Environment[name] = value;
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var server = new SlintServerProcess(process);
        process.Exited += (_, _) => server.exited.TrySetResult(process.ExitCode);
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                onErrorLine(e.Data);
            }
        };

        try
        {
            process.Start();
        }
        catch
        {
            process.Dispose();
            throw;
        }

        process.BeginErrorReadLine();
        return server;
    }

    public async ValueTask DisposeAsync()
    {
        if (!await WaitForExitAsync().ConfigureAwait(false))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            await WaitForExitAsync().ConfigureAwait(false);
        }

        process.Dispose();
    }

    private async Task<bool> WaitForExitAsync()
    {
        try
        {
            await exited.Task.WaitAsync(ExitGracePeriod).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
