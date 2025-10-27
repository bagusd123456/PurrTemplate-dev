using NyxMachina.Shared.EventFramework.Core.Logging;
using NyxMachina.Shared.EventFramework.Core.Messenger;
using NyxMachina.Shared.EventFramework.Core.Payloads;
using System;
using System.Collections.Concurrent;
using System.Linq;

namespace NyxMachina.Shared.EventFramework
{
    public sealed class EventMessenger : IEventMessenger
    {
        private static readonly Lazy<EventMessenger> _lazyInstance = new(() => new EventMessenger());
        public static EventMessenger Instance => _lazyInstance.Value;

        private readonly ConcurrentDictionary<Type, ConcurrentBag<EventSubscriber>> _subscribers;
        private readonly ConcurrentDictionary<Type, IPayload> _payloadStates;
        private ILogger _logger;

        private EventMessenger()
        {
            _subscribers = new ConcurrentDictionary<Type, ConcurrentBag<EventSubscriber>>();
            _payloadStates = new ConcurrentDictionary<Type, IPayload>();
        }

        public void SetLogger(ILogger logger)
        {
            _logger = logger;
        }

        public ISubscription Subscribe<T>(Action<T> callback) where T : IPayload
        {
            var payloadType = typeof(T);
            var subscriberBag = _subscribers.GetOrAdd(payloadType, _ => new ConcurrentBag<EventSubscriber>());

            var subscriber = new EventSubscriber(callback, sub => RemoveSubscriber(payloadType, sub));
            subscriberBag.Add(subscriber);
            return subscriber;
        }

        public IEventMessenger Unsubscribe<T>(Action<T> callback) where T : IPayload
        {
            var payloadType = typeof(T);
            if (!_subscribers.TryGetValue(payloadType, out var subscriberBag))
            {
                return this;
            }

            // Find the subscriber that matches the delegate
            var subscriberToRemove = subscriberBag.FirstOrDefault(s => s.Matches(callback));

            if (subscriberToRemove != null)
            {
                RemoveSubscriber(payloadType, subscriberToRemove);
            } 
            
            return this;
        }

        public IEventMessenger Publish<T>(T payload) where T : IPayload
        {
            if (payload == null) return this;

            var payloadType = payload.GetType();
            _payloadStates[payloadType] = payload;

            if (!_subscribers.TryGetValue(payloadType, out var subscriberBag))
            {
                return this;
            }

            foreach (var subscriber in subscriberBag)
            {
                if (subscriber.IsAlive)
                {
                    try
                    {
                        subscriber.Invoke(payload);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError($"Error in subscriber for event '{payloadType.Name}'.", ex);
                    }
                }
                else
                {
                    subscriber.Dispose();
                }
            }

            return this;
        }
        
        private void RemoveSubscriber(Type payloadType, EventSubscriber subscriberToRemove)
        {
            if (_subscribers.TryGetValue(payloadType, out var subscriberBag))
            {
                // Note: Removing from a ConcurrentBag requires creating a new one.
                // This is a trade-off for high-speed, lock-free publishing.
                var newBag = new ConcurrentBag<EventSubscriber>(subscriberBag.Except(new[] { subscriberToRemove }));
                _subscribers.TryUpdate(payloadType, newBag, subscriberBag);
            }
        }
        
        public T GetState<T>() where T : class, IPayload
        {
            return _payloadStates.TryGetValue(typeof(T), out var payload) ? payload as T : null;
        }
    }
}