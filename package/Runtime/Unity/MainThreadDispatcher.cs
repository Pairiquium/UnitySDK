using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pairiquium.Unity
{
    /// <summary>
    /// Marshals callbacks onto Unity's main thread. MatchmakingClient fires its events from
    /// background async continuations (the receive/heartbeat loops), and Unity API calls are not
    /// thread-safe, so MatchmakingBehaviour routes everything through here before invoking a
    /// UnityEvent.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class MainThreadDispatcher : MonoBehaviour
    {
        private static MainThreadDispatcher _instance;
        private readonly Queue<Action> _pending = new Queue<Action>();
        private readonly object _gate = new object();

        internal static MainThreadDispatcher Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("Pairiquium.MainThreadDispatcher");
                    GameObject.DontDestroyOnLoad(go);
                    _instance = go.AddComponent<MainThreadDispatcher>();
                }

                return _instance;
            }
        }

        internal void Enqueue(Action action)
        {
            lock (_gate)
            {
                _pending.Enqueue(action);
            }
        }

        private void Update()
        {
            while (true)
            {
                Action action;
                lock (_gate)
                {
                    if (_pending.Count == 0)
                    {
                        return;
                    }

                    action = _pending.Dequeue();
                }

                action();
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}
