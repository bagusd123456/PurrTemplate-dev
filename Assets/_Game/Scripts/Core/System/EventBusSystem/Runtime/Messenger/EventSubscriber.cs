using NyxMachina.Shared.EventFramework.Core.Payloads;
using System;
using System.Reflection;

namespace NyxMachina.Shared.EventFramework.Core.Messenger
{
    internal sealed class EventSubscriber : ISubscription
    {
        private readonly WeakReference _target;
        private readonly MethodInfo _method;
        private readonly Action<EventSubscriber> _unsubscribeAction;

        public bool IsAlive => _target.IsAlive;

        public EventSubscriber(Delegate callback, Action<EventSubscriber> unsubscribeAction)
        {
            _target = new WeakReference(callback.Target);
            _method = callback.Method;
            _unsubscribeAction = unsubscribeAction;
        }

        public void Invoke(IPayload payload)
        {
            if (!IsAlive)
            {
                Dispose();
                return;
            }
            _method.Invoke(_target.Target, new object[] { payload });
        }

        // New method to check for delegate equality
        public bool Matches(Delegate callback)
        {
            if (callback == null) return false;
            return _target.Target == callback.Target && _method == callback.Method;
        }

        public void Dispose()
        {
            _unsubscribeAction?.Invoke(this);
        }
    }
}