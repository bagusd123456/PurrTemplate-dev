using System;
using System.Threading;
using System.Threading.Tasks;

public static class TaskEx {
    public static Task<T> WaitAsync<T>(Task<T> task, CancellationToken cancel) {
        if (task.IsCompleted) {
            return task;
        }
        var tcs = new TaskCompletionSource<T>();
        var reg = cancel.Register(() => tcs.TrySetCanceled());
        task.ContinueWith(t => {
            reg.Dispose();
            if (t.IsFaulted) {
                tcs.SetException(t.Exception);
            } else if (t.IsCanceled) {
                tcs.SetCanceled();
            } else {
                tcs.SetResult(t.Result);
            }
        }, cancel);
        return tcs.Task;
    }

    public static Task LogException(this Task task) {
        task.ContinueWith(t => {
            if (t.IsFaulted) {
                Exception ex = t.Exception;
                while (ex is AggregateException agg && agg.InnerExceptions.Count == 1) {
                    ex = agg.InnerExceptions[0];
                }
                UnityEngine.Debug.LogError(ex);
            }
        });
        return task;
    }
}
