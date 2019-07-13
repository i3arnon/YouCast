using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Humanizer;

namespace Service
{
    public class Cache<TKey, TValue>
    {
        private readonly TimeSpan _expiration;
        private readonly ConcurrentDictionary<TKey, (TValue value, DateTime timestamp)> _cache;

        public Cache(TimeSpan expiration)
        {
            _expiration = expiration;
            _cache = new ConcurrentDictionary<TKey, (TValue value, DateTime timestamp)>();
            RunPeriodic(Cleanup, 5.Minutes());
        }

        public bool TryGet(TKey key, out TValue value)
        {
            var exists = _cache.TryGetValue(key, out var cached);
            value = exists 
                ? cached.value 
                : default;

            return exists;
        }

        public void Set(TKey key, TValue value) =>
            _cache[key] = (value, DateTime.UtcNow);

        private void Cleanup()
        {
            var expired = DateTime.UtcNow - _expiration;
            foreach (var (key, value) in _cache)
            {
                if (value.timestamp < expired)
                {
                    _cache.TryRemove(key, out _);
                }
            }
        }

        private static Task RunPeriodic(Action action, TimeSpan interval) =>
            Task.Run(
                async () =>
                {
                    while (true)
                    {
                        try
                        {
                            action();
                        }
                        catch
                        {
                        }

                        await Task.Delay(interval);
                    }
                });
    }

    public static class KeyValuePairExtension
    {
        public static void Deconstruct<TKey, TValue>(
            this KeyValuePair<TKey, TValue> pair,
            out TKey key,
            out TValue value)
        {
            key = pair.Key;
            value = pair.Value;
        }
    }
}
