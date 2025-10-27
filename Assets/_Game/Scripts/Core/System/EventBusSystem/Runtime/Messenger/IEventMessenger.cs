using System;
using NyxMachina.Shared.EventFramework.Core.Payloads;

namespace NyxMachina.Shared.EventFramework.Core.Messenger
{
    /// <summary>
    /// A unified interface for a thread-safe, fault-tolerant event messenger.
    /// </summary>
    public interface IEventMessenger
    {
        /// <summary>
        /// Subscribes a callback to an event of type T.
        /// </summary>
        /// <typeparam name="T">The type of payload to listen for.</typeparam>
        /// <param name="callback">The action to execute when the event is published.</param>
        /// <param name="predicate">An optional filter to determine if the callback should be invoked.</param>
        /// <returns>The messenger instance for fluent chaining.</returns>
        IEventMessenger Subscribe<T>(Action<T> callback, Predicate<T> predicate = null) where T : IPayload;

        /// <summary>
        /// Unsubscribes a callback from an event of type T.
        /// </summary>
        /// <typeparam name="T">The type of payload to unsubscribe from.</typeparam>
        /// <param name="callback">The original callback action to remove.</param>
        /// <returns>The messenger instance for fluent chaining.</returns>
        IEventMessenger Unsubscribe<T>(Action<T> callback) where T : IPayload;

        /// <summary>
        /// Publishes an event payload to all subscribed listeners.
        /// </summary>
        /// <typeparam name="T">The type of the payload.</typeparam>
        /// <param name="payload">The event data to send.</param>
        /// <returns>The messenger instance for fluent chaining.</returns>
        IEventMessenger Publish<T>(T payload) where T : IPayload;

        /// <summary>
        /// Retrieves the last published state for a given payload type.
        /// </summary>
        /// <typeparam name="T">The type of payload state to retrieve.</typeparam>
        /// <returns>The last known payload instance, or null if none exists.</returns>
        T GetState<T>() where T : class, IPayload;
    }
}