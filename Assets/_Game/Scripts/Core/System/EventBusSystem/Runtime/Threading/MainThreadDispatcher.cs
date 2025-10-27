using NyxMachina.Shared.EventFramework.Core.Logging;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace NyxMachina.Shared.EventFramework.Core.Threading
{
    /// <summary>
    /// An engine-agnostic dispatcher that executes actions on the main thread.
    /// </summary>
    public sealed class MainThreadDispatcher : IThreadDispatcher
    {
        private static readonly Lazy<MainThreadDispatcher> _lazyInstance = new(() => new MainThreadDispatcher());
        public static MainThreadDispatcher Instance => _lazyInstance.Value;
        
        // The queue now holds the full task, not just the action.
        private readonly ConcurrentQueue<IDisposable> _queue = new();
        private readonly Stopwatch _stopwatch = new();
        private ILogger _logger;

        public int ThreadId { get; private set; }
        public bool IsMainThread => Thread.CurrentThread.ManagedThreadId == ThreadId;

        private MainThreadDispatcher() 
        {
            // By default, ThreadId will be the thread that first accesses the singleton.
            // Call InitializeOnMainThread() explicitly to guarantee it's the UI thread.
            ThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        /// <summary>
        /// Guarantees the dispatcher's ThreadId is set to the main Unity/UI thread.
        /// Call this once from a main thread context on startup.
        /// </summary>
        public void InitializeOnMainThread(ILogger logger)
        {
            ThreadId = Thread.CurrentThread.ManagedThreadId;
            _logger = logger;
        }

        public void Dispatch(Action action)
        {
            // We can simplify the task object for simple actions
            // or use a more complex one like the original DispatcherTask if payloads are needed.
            // For this use case, a simple Action is sufficient.
            _queue.Enqueue(new DisposableAction(action));
        }

        public void ProcessQueue(long timeBudgetInMs = 5)
        {
            if (!IsMainThread) return;

            _stopwatch.Restart();
            while (_stopwatch.ElapsedMilliseconds < timeBudgetInMs && _queue.TryDequeue(out var task))
            {
                try
                {
                    // The task itself is IDisposable and invokes the action.
                    // Here, we'll assume the task invokes on creation or needs an Invoke method.
                    // For simplicity, let's cast to our internal disposable action type.
                    (task as DisposableAction)?.Invoke();
                }
                catch (Exception ex)
                {
                    _logger?.LogError("Error executing dispatched action.", ex);
                }
                finally
                {
                    // Crucially, dispose the task to prevent resource leaks.
                    task.Dispose();
                }
            }
            _stopwatch.Stop();
        }

        // A simple helper class to make actions disposable in the queue.
        private class DisposableAction : IDisposable
        {
            private Action _action;
            public DisposableAction(Action action) => _action = action;
            public void Invoke() => _action?.Invoke();
            public void Dispose() => _action = null;
        }
    }
}