using System.Collections;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace UniRx.Tests
{
    // Exercises the native (non-Editor) runtime StopCoroutine path only; the Editor-mode
    // EditorThreadDispatcher/PseudoStopCoroutine internals are #if UNITY_EDITOR and stripped from
    // this standalone-player build (see QueueWorkerTest for their ThreadSafeQueueWorker-level coverage).
    public class MainThreadDispatcherStopCoroutineTest
    {
        int steps;

        IEnumerator CountingRoutine()
        {
            while (true)
            {
                steps++;
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator StopCoroutine_PreventsFurtherStepsFromRunning()
        {
            steps = 0;
            var routine = CountingRoutine();
            MainThreadDispatcher.StartCoroutine(routine);

            yield return null;
            var stepsBeforeStop = steps;
            (stepsBeforeStop > 0).IsTrue();

            MainThreadDispatcher.StopCoroutine(routine);

            yield return null;
            yield return null;

            steps.Is(stepsBeforeStop);
        }

        [Test]
        public void StopCoroutine_UnknownRoutine_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => MainThreadDispatcher.StopCoroutine(CountingRoutine()));
        }

        // Characterization test: SendStartCoroutine called from a non-main thread defers its actual
        // StartCoroutine call onto MainThreadDispatcher's queue until the next Update(). One might
        // expect that calling StopCoroutine with the same IEnumerator reference before that deferred
        // call runs would be a no-op (nothing registered with Unity's engine yet) and the coroutine
        // would start anyway. Empirically this does not happen: Unity's own StartCoroutine(IEnumerator)
        // refuses to start a reference previously passed to StopCoroutine(IEnumerator), even one that
        // was never actually running - reproduced with a plain GameObject/MonoBehaviour with no UniRx
        // code involved at all, independent of MainThreadDispatcher's queue or threading. This test
        // pins that existing safe behavior; it is not evidence of a UniRx-side fix, since there is
        // nothing UniRx does here beyond forwarding to Unity's own StopCoroutine/StartCoroutine.
        [UnityTest]
        public IEnumerator StopCoroutine_BeforeDeferredSendStartCoroutineRuns_PreventsItFromStarting()
        {
            steps = 0;
            MainThreadDispatcher.Initialize(); // force lazy-singleton init on the main thread first

            var routine = CountingRoutine();
            bool? sendCalledFromMainThread = null;
            var thread = new Thread(() =>
            {
                // mainThreadToken is [ThreadStatic]: Initialize() above only set it for this test
                // method's own thread, so this read is independent and confirms SendStartCoroutine's
                // deferred (non-main-thread) branch below is the one actually being exercised.
                sendCalledFromMainThread = MainThreadDispatcher.IsInMainThread;
                MainThreadDispatcher.SendStartCoroutine(routine);
            });
            thread.Start();
            thread.Join(); // wait for the background thread's deferred-enqueue call to fully complete

            sendCalledFromMainThread.Value.IsFalse();

            MainThreadDispatcher.StopCoroutine(routine); // no Update() has run yet, so the queued start is still pending

            for (var i = 0; i < 10; i++)
            {
                yield return null;
            }

            steps.Is(0);
        }
    }
}
