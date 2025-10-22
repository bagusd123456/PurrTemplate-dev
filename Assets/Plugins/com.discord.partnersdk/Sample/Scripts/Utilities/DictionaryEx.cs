using System;
using System.Collections.Generic;

public static class DictionaryEx {
    public static TValue Activate<TKey, TValue>(this Dictionary<TKey, TValue> dict,
                                                TKey key,
                                                Func<TValue> factory) {
        if (!dict.TryGetValue(key, out var value)) {
            value = factory();
            dict[key] = value;
        }
        return value;
    }

    public static TValue Activate<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key)
      where TValue : new() { return dict.Activate(key, () => new TValue()); }
}
