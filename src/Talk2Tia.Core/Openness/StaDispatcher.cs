using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Talk2Tia.Core.Openness;

/// <summary>
/// Runs every Openness call on one dedicated STA thread.
/// The Openness API is not thread-safe and some operations require an STA apartment.
/// </summary>
internal sealed class StaDispatcher : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    public StaDispatcher()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "Talk2Tia.Openness" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public bool IsDispatcherThread => Thread.CurrentThread == _thread;

    public Task<T> InvokeAsync<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (IsDispatcherThread)
        {
            Execute(work, tcs);
            return tcs.Task;
        }

        _queue.Add(() => Execute(work, tcs));
        return tcs.Task;
    }

    private static void Execute<T>(Func<T> work, TaskCompletionSource<T> tcs)
    {
        try
        {
            tcs.SetResult(work());
        }
        catch (Exception ex)
        {
            tcs.SetException(ex);
        }
    }

    private void Loop()
    {
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            action();
        }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(10));
        _queue.Dispose();
    }
}
