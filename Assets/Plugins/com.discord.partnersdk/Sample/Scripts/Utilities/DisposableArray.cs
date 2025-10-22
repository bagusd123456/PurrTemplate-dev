using System;
using System.Collections;
using System.Collections.Generic;

public struct DisposableArray<T>
  : IDisposable
  , IEnumerable<T>
  where T : IDisposable {
    public DisposableArray(T[] array) { this.underlying_ = array; }

    public void Dispose() {
        foreach (var item in underlying_) {
            item.Dispose();
        }
    }

    public IEnumerator<T> GetEnumerator() {
        foreach (var item in underlying_) {
            yield return item;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }

    public T[] ToArray() { return underlying_; }

    private readonly T[] underlying_;
}

public static class DisposableArray {
    public static DisposableArray<T> AsDisposable<T>(this T[] array)
      where T : IDisposable { return new DisposableArray<T>(array); }
}
