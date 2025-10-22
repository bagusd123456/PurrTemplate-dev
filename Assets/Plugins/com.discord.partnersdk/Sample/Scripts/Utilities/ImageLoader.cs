using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class ImageLoader : IDisposable {
    private HttpClient client_ = new();
    private Dictionary<string, Task<Texture2D>> cache_ = new();
    private SemaphoreSlim semaphore_ = new SemaphoreSlim(10);

    public Task<Texture2D> Load(string uri) {
        if (cache_.TryGetValue(uri, out var task)) {
            if (!task.IsFaulted && !task.IsCanceled) {
                return task;
            }
        }
        task = LoadInternal(uri);
        cache_[uri] = task;
        return task;
    }

    private async Task<Texture2D> LoadInternal(string uri) {
        await semaphore_.WaitAsync();
        try {
            var bytes = await client_.GetByteArrayAsync(uri);
            var tex = new Texture2D(1, 1);
            ImageConversion.LoadImage(tex, bytes);
            return tex;
        } finally {
            semaphore_.Release();
        }
    }

    public void Dispose() {
        client_.Dispose();
        semaphore_.Dispose();
    }
}
