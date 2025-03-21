using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

/// <summary>
/// Dispatches actions to be executed on the main Unity thread
/// </summary>
public class MainThreadDispatcher : MonoBehaviourSingleton<MainThreadDispatcher> 
{
    private readonly Queue<Action> _executionQueue = new Queue<Action>();
    private readonly object _lockObject = new object();

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }
    
    private void FixedUpdate() 
    {
        lock (_lockObject) 
        {
            while (_executionQueue.Count > 0) 
            {
                _executionQueue.Dequeue().Invoke();
            }
        }
    }

    /// <summary>
    /// Enqueues an action to be executed on the main thread
    /// </summary>
    /// <param name="action">The action to execute on the main thread</param>
    public void Enqueue(Action action) 
    {
        Debug.Log("Received Action");
        if (action == null) 
        {
            Debug.LogError("Cannot enqueue a null action");
            return;
        }

        Debug.Log("Enqueuing Action");
        lock (_lockObject) 
        {
            _executionQueue.Enqueue(action);
        }
    }
}