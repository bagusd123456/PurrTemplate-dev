using System;

namespace NyxMachina.Shared.EventFramework.Core.Logging
{
    /// <summary>
    /// An interface for a generic logging provider to decouple the framework from a specific engine.
    /// </summary>
    public interface ILogger
    {
        void LogError(string message, Exception e = null);
        void LogWarning(string message);
    }
}