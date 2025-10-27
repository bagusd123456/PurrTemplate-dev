using System;

namespace NyxMachina.Shared.EventFramework.Core.Threading
{
    public interface IThreadDispatcher
    {
        int ThreadId { get; }
        void Dispatch(Action action);
    }
}