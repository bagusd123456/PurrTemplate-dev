using System;
using UnityEngine;

namespace NyxMachina.Shared.EventFramework.Unity
{
    /// <summary>
    /// A Unity-specific implementation of the ILogger interface.
    /// </summary>
    public class UnityLogger : Core.Logging.ILogger
    {
        public void LogError(string message, Exception e = null)
        {
            if (e != null)
            {
                Debug.LogError($"{message}\nException: {e}");
            }
            else
            {
                Debug.LogError(message);
            }
        }

        public void LogWarning(string message)
        {
            Debug.LogWarning(message);
        }
    }
}