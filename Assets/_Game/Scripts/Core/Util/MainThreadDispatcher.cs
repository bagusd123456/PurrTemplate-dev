using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MainThreadDispatcher : MonoBehaviour
{
    private static readonly Queue<System.Action> _executionQueue = new Queue<System.Action>();
    private static readonly Queue<CoroutineData> _coroutineQueue = new Queue<CoroutineData>();

    public static MainThreadDispatcher Instance { get; private set; }

    public static MainThreadDispatcher GetInstance()
    {
        if (Instance == null)
        {
            Init();
        }
        return Instance;
    }

    public static void Init()
    {
        var obj = new GameObject("MainThreadDispatcher");
        Instance = obj.AddComponent<MainThreadDispatcher>();
        DontDestroyOnLoad(obj);
    }

    private void Update()
    {
        lock (_executionQueue)
        {
            while (_executionQueue.Count > 0)
            {
                _executionQueue.Dequeue().Invoke();
                var coroutineData = _coroutineQueue.Dequeue();
                StartCoroutine(RunCoroutine(coroutineData.Coroutine, coroutineData.OnComplete));
                //Debug.Log($"Executing Coroutine....");
            }
        }
    }

    private void Enqueue(System.Action action)
    {
        lock (_executionQueue)
        {
            _executionQueue.Enqueue(action);
        }
    }

    public void StartCoroutineOnMainThread(IEnumerator coroutine, Action onComplete = null)
    {
        Enqueue(() => RunCoroutine(coroutine, onComplete));
        var coroutineData = new CoroutineData(coroutine, onComplete);
        _coroutineQueue.Enqueue(coroutineData);
        //Debug.Log($"Coroutine Enqueued....");
    }

    private IEnumerator RunCoroutine(IEnumerator coroutine, System.Action onComplete)
    {
        //Debug.Log($"Running Coroutine....");
        yield return StartCoroutine(coroutine);
        //Detect what method is running
        //while (coroutine.MoveNext())
        //{
        //    Instance.gameObject.name = $"MainThreadDispatcher - Running {coroutine.Current}";
        //}
        Instance.gameObject.name = $"MainThreadDispatcher - [Finished] {coroutine}";
        //Debug.Log($"Coroutine Finished....");
        onComplete?.Invoke();
    }

    public struct CoroutineData
    {
        public IEnumerator Coroutine;
        public Action OnComplete;

        public CoroutineData(IEnumerator coroutine, Action onComplete)
        {
            Coroutine = coroutine;
            this.OnComplete = onComplete;
        }
    }
}