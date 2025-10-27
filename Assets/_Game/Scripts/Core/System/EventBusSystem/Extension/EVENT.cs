using NyxMachina.Shared.EventFramework.Core.Payloads;
using System;

namespace NyxMachina.Shared.EventFramework
{
    public static class EVENT
    {
        public static void Subscribe<T>(Action<T> callback) where T : IPayload
        {
            EventMessenger.Instance.Subscribe(callback);
        }

        public static void Publish<T>(T payload) where T : IPayload
        {
            EventMessenger.Instance.Publish(payload);
        }

        public static void Unsubscribe<T>(Action<T> callback) where T : IPayload
        {
            EventMessenger.Instance.Unsubscribe(callback);
        }
    }
}
