using NyxMachina.Shared.EventFramework.Core.Messenger;
using NyxMachina.Shared.EventFramework.Core.Payloads;
using System;

public interface IEventMessenger
{
    ISubscription Subscribe<T>(Action<T> callback) where T : IPayload;
    IEventMessenger Unsubscribe<T>(Action<T> callback) where T : IPayload;
    IEventMessenger Publish<T>(T payload) where T : IPayload;
    T GetState<T>() where T : class, IPayload;
}