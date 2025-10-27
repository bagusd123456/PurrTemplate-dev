using NyxMachina.Shared.EventFramework.Core.Threading;
using UnityEngine;

namespace NyxMachina.Shared.EventFramework.Unity
{
    /// <summary>
    /// Drives the MainThreadDispatcher and ensures it is properly initialized.
    /// Place this on a persistent GameObject in your scene.
    /// </summary>
    public class MainThreadDispatcherUpdater : MonoBehaviour
    {
        [Tooltip("The maximum time in milliseconds the dispatcher can use per frame.")]
        [SerializeField]
        private long _timeBudgetInMs = 5;

        private void Awake()
        {
            // Ensure the dispatcher knows the main thread and has a logger.
            // This guarantees correct thread affinity and logging behavior.
            var logger = new UnityLogger();
            MainThreadDispatcher.Instance.InitializeOnMainThread(logger);

            // Also set the logger for the EventMessenger instance
            EventMessenger.Instance.SetLogger(logger);
        }

        private void Update()
        {
            MainThreadDispatcher.Instance.ProcessQueue(_timeBudgetInMs);
        }
    }
}