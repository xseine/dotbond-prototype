using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace TranslatorTests.Utilities;

public static class TaskExtensions
{
    public static Task WaitForExitAsync(this Process process,
        CancellationToken cancellationToken = default)
    {
        if (process.HasExited) return Task.CompletedTask;

        var tcs = new TaskCompletionSource<object>();
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => tcs.TrySetResult(null);
        if (cancellationToken != default)
            cancellationToken.Register(() => tcs.SetCanceled(cancellationToken));

        return process.HasExited ? Task.CompletedTask : tcs.Task;
    }
}