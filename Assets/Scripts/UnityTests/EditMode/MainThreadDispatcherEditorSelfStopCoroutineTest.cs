using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace UniRx.Tests
{
    // EditMode-only: reaches MainThreadDispatcher.EditorThreadDispatcher, which is used only when
    // ScenePlaybackDetector.IsPlaying is false. An EditMode test run never enters Play Mode, so
    // IsPlaying stays false for its whole duration and every call below is routed there.
    public class MainThreadDispatcherEditorSelfStopCoroutineTest
    {
        int steps;
        IEnumerator routine;

        IEnumerator SelfStoppingRoutine()
        {
            steps++;
            yield return null;
            steps++;
            MainThreadDispatcher.StopCoroutine(routine); // called from within this step's own execution
            yield return null;
            steps++; // must not run if the stop above actually takes effect
        }

        [UnityTest]
        public IEnumerator StopCoroutine_CalledFromWithinItsOwnStep_PreventsFurtherSteps()
        {
            steps = 0;
            routine = SelfStoppingRoutine();
            MainThreadDispatcher.StartCoroutine(routine);

            for (var i = 0; i < 10; i++)
            {
                yield return null;
            }

            Assert.AreEqual(2, steps);
        }
    }
}
