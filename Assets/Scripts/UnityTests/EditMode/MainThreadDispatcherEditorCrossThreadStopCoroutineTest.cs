using System;
using System.Collections;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace UniRx.Tests
{
    // EditMode-only: reaches MainThreadDispatcher.EditorThreadDispatcher, which is used only when
    // ScenePlaybackDetector.IsPlaying is false. An EditMode test run never enters Play Mode, so
    // IsPlaying stays false for its whole duration and every call below is routed there.
    public class MainThreadDispatcherEditorCrossThreadStopCoroutineTest
    {
        // Blocks inside its own Current getter on the first access, so a background thread can
        // land StopCoroutine on this same IEnumerator reference while ConsumeEnumerator is
        // mid-step -- after MoveNext() already ran but before the next step gets enqueued.
        class CrossThreadStopLandingRoutine : IEnumerator
        {
            public int MoveNextCount;
            public Exception BackgroundException;

            readonly ManualResetEventSlim blockedSignal = new ManualResetEventSlim(false);
            readonly ManualResetEventSlim releaseGate = new ManualResetEventSlim(false);

            public object Current
            {
                get
                {
                    if (MoveNextCount == 1)
                    {
                        blockedSignal.Set();
                        releaseGate.Wait();
                    }
                    return null;
                }
            }

            public bool MoveNext()
            {
                MoveNextCount++;
                if (MoveNextCount == 1)
                {
                    var thread = new Thread(() =>
                    {
                        try
                        {
                            blockedSignal.Wait();
                            MainThreadDispatcher.StopCoroutine(this);
                        }
                        catch (Exception ex)
                        {
                            BackgroundException = ex;
                        }
                        finally
                        {
                            releaseGate.Set();
                        }
                    });
                    thread.IsBackground = true;
                    thread.Start();
                }
                return true;
            }

            public void Reset()
            {
                throw new NotSupportedException();
            }
        }

        [UnityTest]
        public IEnumerator StopCoroutine_LandingWhileConsumeEnumeratorReadsCurrent_PreventsFurtherSteps()
        {
            var routine = new CrossThreadStopLandingRoutine();
            MainThreadDispatcher.StartCoroutine(routine);

            for (var i = 0; i < 10; i++)
            {
                yield return null;
            }

            Assert.IsNull(routine.BackgroundException);
            // Pre-fix, the step in flight when the stop landed still enqueued its next step
            // unconditionally, so MoveNext ran a second time before the following tick's
            // IsRoutineActive check caught it -- MoveNextCount would be 2.
            Assert.AreEqual(1, routine.MoveNextCount);
        }
    }
}
