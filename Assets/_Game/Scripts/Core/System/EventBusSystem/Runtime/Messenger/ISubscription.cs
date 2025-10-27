using System;

namespace NyxMachina.Shared.EventFramework.Core.Messenger
{
    /// <summary>
    /// Represents a subscription to an event. Disposing this object will unsubscribe from the event.
    /// </summary>
    public interface ISubscription : IDisposable
    {
    }
}