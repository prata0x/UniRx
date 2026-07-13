#if !(UNITY_4_0 || UNITY_4_1 || UNITY_4_2 || UNITY_4_3 || UNITY_4_4 || UNITY_4_5 || UNITY_4_6 || UNITY_5_0 || UNITY_5_1 || UNITY_5_2)
#define SupportCustomYieldInstruction
#endif

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using UniRx.InternalUtil;
using UnityEngine;

namespace UniRx
{
    public sealed class MainThreadDispatcher : MonoBehaviour
    {
        public enum CullingMode
        {
            /// <summary>
            /// Won't remove any MainThreadDispatchers.
            /// </summary>
            Disabled,

            /// <summary>
            /// Checks if there is an existing MainThreadDispatcher on Awake(). If so, the new dispatcher removes itself.
            /// </summary>
            Self,

            /// <summary>
            /// Search for excess MainThreadDispatchers and removes them all on Awake().
            /// </summary>
            All
        }

        public static CullingMode cullingMode = CullingMode.Self;

#if UNITY_EDITOR

        // In UnityEditor's EditorMode can't instantiate and work MonoBehaviour.Update.
        // EditorThreadDispatcher use EditorApplication.update instead of MonoBehaviour.Update.
        class EditorThreadDispatcher
        {
            static object gate = new object();
            static EditorThreadDispatcher instance;

            public static EditorThreadDispatcher Instance
            {
                get
                {
                    // Activate EditorThreadDispatcher is dangerous, completely Lazy.
                    lock (gate)
                    {
                        if (instance == null)
                        {
                            instance = new EditorThreadDispatcher();
                        }

                        return instance;
                    }
                }
            }

            ThreadSafeQueueWorker editorQueueWorker = new ThreadSafeQueueWorker();

            object routineIdMapGate = new object();
            Dictionary<IEnumerator, object> routineIdMap = new Dictionary<IEnumerator, object>();

            EditorThreadDispatcher()
            {
                UnityEditor.EditorApplication.update += Update;
            }

            public void Enqueue(Action<object> action, object state)
            {
                editorQueueWorker.Enqueue(action, state);
            }

            public void UnsafeInvoke(Action action)
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }

            public void UnsafeInvoke<T>(Action<T> action, T state)
            {
                try
                {
                    action(state);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }

            public void PseudoStartCoroutine(IEnumerator routine)
            {
                var routineId = new object();
                // Registration and the first Enqueue must happen atomically: if a PseudoStopCoroutine
                // landed between them, its routineIdMap removal would have no queued action to find,
                // letting this first step run after StopCoroutine already returned to its caller.
                lock (routineIdMapGate)
                {
                    routineIdMap[routine] = routineId;
                    editorQueueWorker.Enqueue(_ => ConsumeEnumerator(routine, routine, routineId), routineId);
                }
            }

            /// <summary>
            /// Stops a coroutine previously started via PseudoStartCoroutine, matched by the same
            /// IEnumerator reference (mirrors MonoBehaviour.StartCoroutine/StopCoroutine's own
            /// reference-based matching). Coroutines started via StartUpdateMicroCoroutine and its
            /// FixedUpdate/EndOfFrame siblings don't go through this path and can't be stopped here.
            /// No-ops (does not throw) for an unknown or already-finished routine, matching Unity's
            /// own StopCoroutine behavior. Only cancels work not yet started: a step already
            /// executing when this is called still runs to completion, matching Unity's own
            /// StopCoroutine.
            /// </summary>
            public void PseudoStopCoroutine(IEnumerator routine)
            {
                object routineId;
                lock (routineIdMapGate)
                {
                    if (!routineIdMap.TryGetValue(routine, out routineId))
                    {
                        return;
                    }
                    routineIdMap.Remove(routine);
                }
                editorQueueWorker.RemoveActionByState(routineId);
            }

            void Update()
            {
                editorQueueWorker.ExecuteAll(x => Debug.LogException(x));
            }

            // A routine can be stopped (PseudoStopCoroutine removing its routineIdMap entry)
            // while its current step is already executing -- e.g. it calls StopCoroutine on
            // itself, or a stop lands on another thread mid-step. RemoveActionByState() then has
            // nothing queued to remove, so without this check the step below would still
            // re-enqueue the next frame's work under a routineId that's already gone, making the
            // routine unstoppable (routineIdMap no longer has an entry for it, so any further
            // PseudoStopCoroutine call is a silent no-op) instead of actually stopping.
            bool IsRoutineActive(IEnumerator rootRoutine, object routineId)
            {
                lock (routineIdMapGate)
                {
                    return IsRoutineActiveUnsafe(rootRoutine, routineId);
                }
            }

            bool IsRoutineActiveUnsafe(IEnumerator rootRoutine, object routineId)
            {
                object currentRoutineId;
                return routineIdMap.TryGetValue(rootRoutine, out currentRoutineId) && ReferenceEquals(currentRoutineId, routineId);
            }

            // Checking IsRoutineActive and calling Enqueue as two separate steps leaves a window
            // where a PseudoStopCoroutine landing between them removes the routineIdMap entry but
            // finds nothing queued yet to remove, so the stale continuation still gets enqueued.
            // Folding both into one routineIdMapGate lock closes that window. Lock order here is
            // always routineIdMapGate -> ThreadSafeQueueWorker's own internal gate; nothing ever
            // takes them in the reverse order, so this can't deadlock.
            void EnqueueIfActive(IEnumerator rootRoutine, object routineId, Action<object> action)
            {
                lock (routineIdMapGate)
                {
                    if (IsRoutineActiveUnsafe(rootRoutine, routineId))
                    {
                        editorQueueWorker.Enqueue(action, routineId);
                    }
                }
            }

            void ConsumeEnumerator(IEnumerator routine, IEnumerator rootRoutine, object routineId)
            {
                bool hasNext;
                try
                {
                    hasNext = routine.MoveNext();
                }
                catch
                {
                    // Unconditional unlike the natural-completion branch below: a throw can surface
                    // here with `routine` bound to an inner Unwrap* wrapper rather than rootRoutine,
                    // but it always means rootRoutine's whole chain is dead (Remove is a no-op if
                    // an inner frame already removed it).
                    lock (routineIdMapGate)
                    {
                        routineIdMap.Remove(rootRoutine);
                    }
                    throw;
                }

                if (hasNext)
                {
                    if (!IsRoutineActive(rootRoutine, routineId))
                    {
                        // Stopped while the step above (routine.MoveNext()) was executing --
                        // e.g. the routine called StopCoroutine on itself. RemoveActionByState()
                        // in PseudoStopCoroutine had nothing queued to remove at that point, so
                        // this is the only place left to actually stop the chain.
                        return;
                    }

                    var current = routine.Current;
                    if (current == null)
                    {
                        goto ENQUEUE;
                    }

                    var type = current.GetType();
#if !UNITY_2019_1_OR_NEWER || UNIRX_WWW_SUPPORT
#if UNITY_2018_3_OR_NEWER
#pragma warning disable CS0618
#endif
                    if (type == typeof(WWW))
                    {
                        var www = (WWW)current;
                        EnqueueIfActive(rootRoutine, routineId, _ => ConsumeEnumerator(UnwrapWaitWWW(www, routine, rootRoutine, routineId), rootRoutine, routineId));
                        return;
                    }
                    else
#if UNITY_2018_3_OR_NEWER
#pragma warning restore CS0618
#endif
#endif
                    if (typeof(AsyncOperation).IsAssignableFrom(type))
                    {
                        var asyncOperation = (AsyncOperation)current;
                        EnqueueIfActive(rootRoutine, routineId, _ => ConsumeEnumerator(UnwrapWaitAsyncOperation(asyncOperation, routine, rootRoutine, routineId), rootRoutine, routineId));
                        return;
                    }
                    else if (type == typeof(WaitForSeconds))
                    {
                        var waitForSeconds = (WaitForSeconds)current;
                        var accessor = typeof(WaitForSeconds).GetField("m_Seconds", BindingFlags.Instance | BindingFlags.GetField | BindingFlags.NonPublic);
                        var second = (float)accessor.GetValue(waitForSeconds);
                        EnqueueIfActive(rootRoutine, routineId, _ => ConsumeEnumerator(UnwrapWaitForSeconds(second, routine, rootRoutine, routineId), rootRoutine, routineId));
                        return;
                    }
                    else if (type == typeof(Coroutine))
                    {
                        Debug.Log("Can't wait coroutine on UnityEditor");
                        goto ENQUEUE;
                    }
#if SupportCustomYieldInstruction
                    else if (current is IEnumerator)
                    {
                        var enumerator = (IEnumerator)current;
                        EnqueueIfActive(rootRoutine, routineId, _ => ConsumeEnumerator(UnwrapEnumerator(enumerator, routine, rootRoutine, routineId), rootRoutine, routineId));
                        return;
                    }
#endif

                    ENQUEUE:
                    editorQueueWorker.Enqueue(_ => ConsumeEnumerator(routine, rootRoutine, routineId), routineId); // next update -- SCRATCH: reverted to verify old-fail
                }
                else if (ReferenceEquals(routine, rootRoutine))
                {
                    // the whole coroutine (not just an internal wait-unwrapper) has no more steps;
                    // nothing further will ever be enqueued under this routineId
                    lock (routineIdMapGate)
                    {
                        routineIdMap.Remove(rootRoutine);
                    }
                }
            }

#if !UNITY_2019_1_OR_NEWER || UNIRX_WWW_SUPPORT
#if UNITY_2018_3_OR_NEWER
#pragma warning disable CS0618
#endif
            IEnumerator UnwrapWaitWWW(WWW www, IEnumerator continuation, IEnumerator rootRoutine, object routineId)
            {
                while (!www.isDone)
                {
                    yield return null;
                }
                ConsumeEnumerator(continuation, rootRoutine, routineId);
            }
#if UNITY_2018_3_OR_NEWER
#pragma warning restore CS0618
#endif
#endif

            IEnumerator UnwrapWaitAsyncOperation(AsyncOperation asyncOperation, IEnumerator continuation, IEnumerator rootRoutine, object routineId)
            {
                while (!asyncOperation.isDone)
                {
                    yield return null;
                }
                ConsumeEnumerator(continuation, rootRoutine, routineId);
            }

            IEnumerator UnwrapWaitForSeconds(float second, IEnumerator continuation, IEnumerator rootRoutine, object routineId)
            {
                var startTime = DateTimeOffset.UtcNow;
                while (true)
                {
                    yield return null;

                    var elapsed = (DateTimeOffset.UtcNow - startTime).TotalSeconds;
                    if (elapsed >= second)
                    {
                        break;
                    }
                };
                ConsumeEnumerator(continuation, rootRoutine, routineId);
            }

            IEnumerator UnwrapEnumerator(IEnumerator enumerator, IEnumerator continuation, IEnumerator rootRoutine, object routineId)
            {
                while (enumerator.MoveNext())
                {
                    yield return null;
                }
                ConsumeEnumerator(continuation, rootRoutine, routineId);
            }
        }

#endif

        /// <summary>Dispatch Asyncrhonous action.</summary>
        public static void Post(Action<object> action, object state)
        {
#if UNITY_EDITOR
            if (!ScenePlaybackDetector.IsPlaying) { EditorThreadDispatcher.Instance.Enqueue(action, state); return; }

#endif

            var dispatcher = Instance;
            if (!isQuitting && !object.ReferenceEquals(dispatcher, null))
            {
                dispatcher.queueWorker.Enqueue(action, state);
            }
        }

        /// <summary>Dispatch Synchronous action if possible.</summary>
        public static void Send(Action<object> action, object state)
        {
#if UNITY_EDITOR
            if (!ScenePlaybackDetector.IsPlaying) { EditorThreadDispatcher.Instance.Enqueue(action, state); return; }
#endif

            if (mainThreadToken != null)
            {
                try
                {
                    action(state);
                }
                catch (Exception ex)
                {
                    var dispatcher = MainThreadDispatcher.Instance;
                    if (dispatcher != null)
                    {
                        dispatcher.unhandledExceptionCallback(ex);
                    }
                }
            }
            else
            {
                Post(action, state);
            }
        }

        /// <summary>Run Synchronous action.</summary>
        public static void UnsafeSend(Action action)
        {
#if UNITY_EDITOR
            if (!ScenePlaybackDetector.IsPlaying) { EditorThreadDispatcher.Instance.UnsafeInvoke(action); return; }
#endif

            try
            {
                action();
            }
            catch (Exception ex)
            {
                var dispatcher = MainThreadDispatcher.Instance;
                if (dispatcher != null)
                {
                    dispatcher.unhandledExceptionCallback(ex);
                }
            }
        }

        /// <summary>Run Synchronous action.</summary>
        public static void UnsafeSend<T>(Action<T> action, T state)
        {
#if UNITY_EDITOR
            if (!ScenePlaybackDetector.IsPlaying) { EditorThreadDispatcher.Instance.UnsafeInvoke(action, state); return; }
#endif

            try
            {
                action(state);
            }
            catch (Exception ex)
            {
                var dispatcher = MainThreadDispatcher.Instance;
                if (dispatcher != null)
                {
                    dispatcher.unhandledExceptionCallback(ex);
                }
            }
        }

        /// <summary>ThreadSafe StartCoroutine.</summary>
        public static void SendStartCoroutine(IEnumerator routine)
        {
            if (mainThreadToken != null)
            {
                StartCoroutine(routine);
            }
            else
            {
#if UNITY_EDITOR
                // call from other thread
                if (!ScenePlaybackDetector.IsPlaying) { EditorThreadDispatcher.Instance.PseudoStartCoroutine(routine); return; }
#endif

                var dispatcher = Instance;
                if (!isQuitting && !object.ReferenceEquals(dispatcher, null))
                {
                    dispatcher.queueWorker.Enqueue(_ =>
                    {
                        var dispacher2 = Instance;
                        if (dispacher2 != null)
                        {
                            (dispacher2 as MonoBehaviour).StartCoroutine(routine);
                        }
                    }, null);
                }
            }
        }

        public static void StartUpdateMicroCoroutine(IEnumerator routine)
        {
#if UNITY_EDITOR
            if (!ScenePlaybackDetector.IsPlaying) { EditorThreadDispatcher.Instance.PseudoStartCoroutine(routine); return; }
#endif

            var dispatcher = Instance;
            if (dispatcher != null)
            {
                dispatcher.updateMicroCoroutine.AddCoroutine(routine);
            }
        }

        public static void StartFixedUpdateMicroCoroutine(IEnumerator routine)
        {
#if UNITY_EDITOR
            if (!ScenePlaybackDetector.IsPlaying) { EditorThreadDispatcher.Instance.PseudoStartCoroutine(routine); return; }
#endif

            var dispatcher = Instance;
            if (dispatcher != null)
            {
                dispatcher.fixedUpdateMicroCoroutine.AddCoroutine(routine);
            }
        }

        public static void StartEndOfFrameMicroCoroutine(IEnumerator routine)
        {
#if UNITY_EDITOR
            if (!ScenePlaybackDetector.IsPlaying) { EditorThreadDispatcher.Instance.PseudoStartCoroutine(routine); return; }
#endif

            var dispatcher = Instance;
            if (dispatcher != null)
            {
                dispatcher.endOfFrameMicroCoroutine.AddCoroutine(routine);
            }
        }

        new public static Coroutine StartCoroutine(IEnumerator routine)
        {
#if UNITY_EDITOR
            if (!ScenePlaybackDetector.IsPlaying) { EditorThreadDispatcher.Instance.PseudoStartCoroutine(routine); return null; }
#endif

            var dispatcher = Instance;
            if (dispatcher != null)
            {
                return (dispatcher as MonoBehaviour).StartCoroutine(routine);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Stops a coroutine previously started via StartCoroutine/SendStartCoroutine, matched by the
        /// same IEnumerator reference (mirrors MonoBehaviour.StopCoroutine's own reference-based
        /// matching). Coroutines started via StartUpdateMicroCoroutine and its FixedUpdate/EndOfFrame
        /// siblings run on a separate dispatch path and can't be stopped through this method.
        /// If SendStartCoroutine was called from a non-main thread, its actual StartCoroutine call is
        /// deferred to a later frame; calling StopCoroutine with the same routine before that deferred
        /// call runs still prevents it from starting, since the underlying Unity engine refuses to
        /// start a routine reference previously passed to StopCoroutine even if it was never running.
        /// </summary>
        new public static void StopCoroutine(IEnumerator routine)
        {
#if UNITY_EDITOR
            if (!ScenePlaybackDetector.IsPlaying) { EditorThreadDispatcher.Instance.PseudoStopCoroutine(routine); return; }
#endif

            var dispatcher = Instance;
            if (dispatcher != null)
            {
                (dispatcher as MonoBehaviour).StopCoroutine(routine);
            }
        }

        public static void RegisterUnhandledExceptionCallback(Action<Exception> exceptionCallback)
        {
            if (exceptionCallback == null)
            {
                // do nothing
                Instance.unhandledExceptionCallback = Stubs<Exception>.Ignore;
            }
            else
            {
                Instance.unhandledExceptionCallback = exceptionCallback;
            }
        }

        ThreadSafeQueueWorker queueWorker = new ThreadSafeQueueWorker();
        Action<Exception> unhandledExceptionCallback = ex => Debug.LogException(ex); // default

        MicroCoroutine updateMicroCoroutine = null;
        MicroCoroutine fixedUpdateMicroCoroutine = null;
        MicroCoroutine endOfFrameMicroCoroutine = null;

        static MainThreadDispatcher instance;
        static bool initialized;
        static bool isQuitting = false;

        public static string InstanceName
        {
            get
            {
                if (instance == null)
                {
                    throw new NullReferenceException("MainThreadDispatcher is not initialized.");
                }
                return instance.name;
            }
        }

        public static bool IsInitialized
        {
            get { return initialized && instance != null; }
        }

        [ThreadStatic]
        static object mainThreadToken;

        static MainThreadDispatcher Instance
        {
            get
            {
                Initialize();
                return instance;
            }
        }

#if UNITY_2019_3_OR_NEWER && UNITY_EDITOR
        // Clean up static properties for times when Domain Reload is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void DomainCleanup()
        {
            initialized = false;
            isQuitting = false;
            instance = null;
        }
#endif

        public static void Initialize()
        {
            if (!initialized)
            {
#if UNITY_EDITOR
                // Don't try to add a GameObject when the scene is not playing. Only valid in the Editor, EditorView.
                if (!ScenePlaybackDetector.IsPlaying) return;
#endif
                if (isQuitting)
                {
                    // don't create new instance after quitting
                    // avoid "Some objects were not cleaned up when closing the scene find target" error.
                    return;
                }

                MainThreadDispatcher dispatcher = null;

                try
                {
#if UNITY_2020_3_OR_NEWER
                    dispatcher = GameObject.FindAnyObjectByType<MainThreadDispatcher>();
#else
                    dispatcher = GameObject.FindObjectOfType<MainThreadDispatcher>();
#endif
                }
                catch
                {
                    // Throw exception when calling from a worker thread.
                    var ex = new Exception("UniRx requires a MainThreadDispatcher component created on the main thread. Make sure it is added to the scene before calling UniRx from a worker thread.");
                    UnityEngine.Debug.LogException(ex);
                    throw ex;
                }

                if (dispatcher == null)
                {
                    // awake call immediately from UnityEngine
                    new GameObject("MainThreadDispatcher").AddComponent<MainThreadDispatcher>();
                }
                else
                {
                    dispatcher.Awake(); // force awake
                }
            }
        }

        public static bool IsInMainThread
        {
            get
            {
                return (mainThreadToken != null);
            }
        }

        void Awake()
        {
            if (instance == null)
            {
                instance = this;
                mainThreadToken = new object();
                initialized = true;

                updateMicroCoroutine = new MicroCoroutine(ex => unhandledExceptionCallback(ex));
                fixedUpdateMicroCoroutine = new MicroCoroutine(ex => unhandledExceptionCallback(ex));
                endOfFrameMicroCoroutine = new MicroCoroutine(ex => unhandledExceptionCallback(ex));

#if UNITY_2018_1_OR_NEWER
                Application.quitting += OnApplicationQuitting;
#endif

                StartCoroutine(RunUpdateMicroCoroutine());
                StartCoroutine(RunFixedUpdateMicroCoroutine());
                StartCoroutine(RunEndOfFrameMicroCoroutine());

                DontDestroyOnLoad(gameObject);
            }
            else
            {
                if (this != instance)
                {
                    if (cullingMode == CullingMode.Self)
                    {
                        // Try to destroy this dispatcher if there's already one in the scene.
                        Debug.LogWarning("There is already a MainThreadDispatcher in the scene. Removing myself...");
                        DestroyDispatcher(this);
                    }
                    else if (cullingMode == CullingMode.All)
                    {
                        Debug.LogWarning("There is already a MainThreadDispatcher in the scene. Cleaning up all excess dispatchers...");
                        CullAllExcessDispatchers();
                    }
                    else
                    {
                        Debug.LogWarning("There is already a MainThreadDispatcher in the scene.");
                    }
                }
            }
        }

        IEnumerator RunUpdateMicroCoroutine()
        {
            while (true)
            {
                yield return null;
                updateMicroCoroutine.Run();
            }
        }

        IEnumerator RunFixedUpdateMicroCoroutine()
        {
            while (true)
            {
                yield return YieldInstructionCache.WaitForFixedUpdate;
                fixedUpdateMicroCoroutine.Run();
            }
        }

        IEnumerator RunEndOfFrameMicroCoroutine()
        {
            while (true)
            {
                yield return YieldInstructionCache.WaitForEndOfFrame;
                endOfFrameMicroCoroutine.Run();
            }
        }

        static void DestroyDispatcher(MainThreadDispatcher aDispatcher)
        {
            if (aDispatcher != instance)
            {
                // Try to remove game object if it's empty
                var components = aDispatcher.gameObject.GetComponents<Component>();
                if (aDispatcher.gameObject.transform.childCount == 0 && components.Length == 2)
                {
                    if (components[0] is Transform && components[1] is MainThreadDispatcher)
                    {
                        Destroy(aDispatcher.gameObject);
                    }
                }
                else
                {
                    // Remove component
                    MonoBehaviour.Destroy(aDispatcher);
                }
            }
        }

        public static void CullAllExcessDispatchers()
        {
#if UNITY_2020_3_OR_NEWER
            var dispatchers = GameObject.FindObjectsByType<MainThreadDispatcher>(FindObjectsSortMode.None);
#else
            var dispatchers = GameObject.FindObjectsOfType<MainThreadDispatcher>();
#endif
            for (int i = 0; i < dispatchers.Length; i++)
            {
                DestroyDispatcher(dispatchers[i]);
            }
        }

        void OnDestroy()
        {
            if (instance == this)
            {
#if UNITY_2020_3_OR_NEWER
                instance = GameObject.FindAnyObjectByType<MainThreadDispatcher>();
#else
                instance = GameObject.FindObjectOfType<MainThreadDispatcher>();
#endif
                initialized = instance != null;
#if UNITY_2018_1_OR_NEWER
                Application.quitting -= OnApplicationQuitting;
#endif

                /*
                // Although `this` still refers to a gameObject, it won't be found.
                var foundDispatcher = GameObject.FindObjectOfType<MainThreadDispatcher>();

                if (foundDispatcher != null)
                {
                    // select another game object
                    Debug.Log("new instance: " + foundDispatcher.name);
                    instance = foundDispatcher;
                    initialized = true;
                }
                */
            }
        }

        void Update()
        {
            if (update != null)
            {
                try
                {
                    update.OnNext(Unit.Default);
                }
                catch (Exception ex)
                {
                    unhandledExceptionCallback(ex);
                }
            }
            queueWorker.ExecuteAll(unhandledExceptionCallback);
        }

        // for Lifecycle Management

        Subject<Unit> update;

        public static IObservable<Unit> UpdateAsObservable()
        {
            return Instance.update ?? (Instance.update = new Subject<Unit>());
        }

        Subject<Unit> lateUpdate;

        void LateUpdate()
        {
            if (lateUpdate != null) lateUpdate.OnNext(Unit.Default);
        }

        public static IObservable<Unit> LateUpdateAsObservable()
        {
            return Instance.lateUpdate ?? (Instance.lateUpdate = new Subject<Unit>());
        }

        Subject<bool> onApplicationFocus;

        void OnApplicationFocus(bool focus)
        {
            if (onApplicationFocus != null) onApplicationFocus.OnNext(focus);
        }

        public static IObservable<bool> OnApplicationFocusAsObservable()
        {
            return Instance.onApplicationFocus ?? (Instance.onApplicationFocus = new Subject<bool>());
        }

        Subject<bool> onApplicationPause;

        void OnApplicationPause(bool pause)
        {
            if (onApplicationPause != null) onApplicationPause.OnNext(pause);
        }

        public static IObservable<bool> OnApplicationPauseAsObservable()
        {
            return Instance.onApplicationPause ?? (Instance.onApplicationPause = new Subject<bool>());
        }

        Subject<Unit> onApplicationQuit;

#if UNITY_2018_1_OR_NEWER
        void OnApplicationQuitting()
#else
        void OnApplicationQuit()
#endif
        {
            isQuitting = true;
            if (onApplicationQuit != null) onApplicationQuit.OnNext(Unit.Default);
        }

        public static IObservable<Unit> OnApplicationQuitAsObservable()
        {
            return Instance.onApplicationQuit ?? (Instance.onApplicationQuit = new Subject<Unit>());
        }
    }
}