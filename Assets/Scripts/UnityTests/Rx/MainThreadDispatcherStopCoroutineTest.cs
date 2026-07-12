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

        // DIAGNOSTIC (temporary): checks whether calling StopCoroutine on an IEnumerator that was
        // never actually registered with Unity's coroutine engine disposes/terminates it anyway,
        // which would make a later StartCoroutine(sameRoutine) silently do nothing regardless of
        // any pending-registration fix.
        [Test]
        public void Diagnostic_StopCoroutine_OnNeverStartedRoutine_DoesNotDisposeIt()
        {
            steps = 0;
            var routine = CountingRoutine();

            MainThreadDispatcher.StopCoroutine(routine); // never started via StartCoroutine/SendStartCoroutine

            var hasNext = routine.MoveNext();

            hasNext.IsTrue();
            steps.Is(1);
        }

        // DIAGNOSTIC (temporary): confirms mainThreadToken ([ThreadStatic]) actually reads as unset
        // on a genuinely different OS thread, so SendStartCoroutine's non-main-thread branch is the
        // one really being exercised below rather than silently falling through to the synchronous one.
        [Test]
        public void Diagnostic_IsInMainThread_FalseOnBackgroundThread()
        {
            MainThreadDispatcher.Initialize();
            bool? isMainThreadOnBackgroundThread = null;
            var thread = new Thread(() => { isMainThreadOnBackgroundThread = MainThreadDispatcher.IsInMainThread; });
            thread.Start();
            thread.Join();

            isMainThreadOnBackgroundThread.HasValue.IsTrue();
            isMainThreadOnBackgroundThread.Value.IsFalse();
        }

        // DIAGNOSTIC (temporary): checks whether SendStartCoroutine's deferred registration actually
        // runs the routine at all (without ever calling StopCoroutine), to isolate whether a failure
        // in the real regression test comes from the deferred-start mechanism itself versus from
        // StopCoroutine's interaction with it.
        [UnityTest]
        public IEnumerator Diagnostic_SendStartCoroutineFromOtherThread_EventuallyRunsWithoutStop()
        {
            steps = 0;
            MainThreadDispatcher.Initialize();

            var routine = CountingRoutine();
            var thread = new Thread(() => MainThreadDispatcher.SendStartCoroutine(routine));
            thread.Start();
            thread.Join();

            yield return null;
            yield return null;
            yield return null;

            (steps > 0).IsTrue();
        }

        // SendStartCoroutine called from a non-main thread defers its actual StartCoroutine call
        // onto MainThreadDispatcher's queue until the next Update(). This does not test general
        // thread-safety of StopCoroutine itself (which remains main-thread-only) - only that a
        // main-thread StopCoroutine call landing before that deferred start runs can still cancel it.
        [UnityTest]
        public IEnumerator StopCoroutine_CancelsPendingSendStartCoroutineFromOtherThread()
        {
            steps = 0;
            MainThreadDispatcher.Initialize(); // force lazy-singleton init on the main thread first

            var routine = CountingRoutine();
            var thread = new Thread(() => MainThreadDispatcher.SendStartCoroutine(routine));
            thread.Start();
            thread.Join(); // wait for the background thread's deferred-enqueue call to fully complete

            MainThreadDispatcher.StopCoroutine(routine); // no Update() has run yet, so the queued start is still pending

            yield return null;
            yield return null;

            steps.Is(0);
        }
    }
}
