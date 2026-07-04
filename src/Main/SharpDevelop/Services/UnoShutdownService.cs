using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.SharpDevelop.Workbench;

namespace UnoDevelop.Services;

internal sealed class UnoShutdownService : IShutdownService
{
    private readonly ConcurrentDictionary<Guid, string> _reasons = new();
    private readonly ConcurrentBag<Task> _backgroundTasks = new();
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly CancellationTokenSource _delayedShutdownCts = new();

    private int _isShuttingDown;

    public string CurrentReasonPreventingShutdown => _reasons.Values.FirstOrDefault() ?? string.Empty;

    public CancellationToken ShutdownToken => _shutdownCts.Token;

    public CancellationToken DelayedShutdownToken => _delayedShutdownCts.Token;

    public IDisposable PreventShutdown(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A shutdown prevention reason is required.", nameof(reason));
        }

        if (Volatile.Read(ref _isShuttingDown) == 1)
        {
            throw new InvalidOperationException("Shutdown is already in progress.");
        }

        var key = Guid.NewGuid();
        _reasons[key] = reason;
        return new ReleaseHandle(_reasons, key);
    }

    public void AddBackgroundTask(Task task)
    {
        if (task is null)
        {
            throw new ArgumentNullException(nameof(task));
        }

        _backgroundTasks.Add(task);
    }

    public bool Shutdown()
    {
        if (!_reasons.IsEmpty)
        {
            return false;
        }

        if (Interlocked.Exchange(ref _isShuttingDown, 1) == 1)
        {
            return false;
        }

        _shutdownCts.Cancel();
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            _delayedShutdownCts.Cancel();
        });

        Task[] running = _backgroundTasks.Where(t => t is not null).ToArray();
        if (running.Length > 0)
        {
            try
            {
                Task.WaitAll(running, TimeSpan.FromSeconds(10));
            }
            catch
            {
                // Best effort on shutdown.
            }
        }

        return true;
    }

    private sealed class ReleaseHandle : IDisposable
    {
        private readonly ConcurrentDictionary<Guid, string> _store;
        private readonly Guid _key;
        private int _disposed;

        public ReleaseHandle(ConcurrentDictionary<Guid, string> store, Guid key)
        {
            _store = store;
            _key = key;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            _store.TryRemove(_key, out _);
        }
    }
}
