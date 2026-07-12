using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using NUnit.Framework;

namespace UniRx.Tests
{
    public class ObservableFromCoroutineValueTest
    {
        class ThrowingEnumerator : IEnumerator
        {
            readonly Exception exception;
            bool thrown;

            public ThrowingEnumerator(Exception exception)
            {
                this.exception = exception;
            }

            public object Current { get { return null; } }

            public bool MoveNext()
            {
                if (!thrown)
                {
                    thrown = true;
                    throw exception;
                }
                return false;
            }

            public void Reset()
            {
                throw new NotSupportedException();
            }
        }

        class ValueEnumerator : IEnumerator
        {
            readonly object[] values;
            int index = -1;

            public ValueEnumerator(params object[] values)
            {
                this.values = values;
            }

            public object Current { get { return values[index]; } }

            public bool MoveNext()
            {
                index++;
                return index < values.Length;
            }

            public void Reset()
            {
                throw new NotSupportedException();
            }
        }

        class RecordingObserver<T> : IObserver<T>
        {
            public readonly List<T> Values = new List<T>();
            public int OnErrorCallCount;
            public int OnCompletedCallCount;
            public Exception Error;

            public void OnNext(T value)
            {
                Values.Add(value);
            }

            public void OnError(Exception error)
            {
                OnErrorCallCount++;
                Error = error;
            }

            public void OnCompleted()
            {
                OnCompletedCallCount++;
            }
        }

        static IEnumerator InvokeWrapEnumeratorYieldValue<T>(IEnumerator enumerator, IObserver<T> observer, bool nullAsNextUpdate = true)
        {
            var method = typeof(Observable).GetMethod("WrapEnumeratorYieldValue", BindingFlags.NonPublic | BindingFlags.Static);
            var generic = method.MakeGenericMethod(typeof(T));
            return (IEnumerator)generic.Invoke(null, new object[] { enumerator, observer, CancellationToken.None, nullAsNextUpdate });
        }

        static IEnumerator InvokeWrapToCancellableEnumerator<T>(IEnumerator enumerator, IObserver<T> observer)
        {
            var method = typeof(Observable).GetMethod("WrapToCancellableEnumerator", BindingFlags.NonPublic | BindingFlags.Static);
            var generic = method.MakeGenericMethod(typeof(T));
            return (IEnumerator)generic.Invoke(null, new object[] { enumerator, observer, CancellationToken.None });
        }

        // Mimics Unity's native nested-coroutine execution: when Current is an IEnumerator,
        // recursively drives it. If the nested drive throws, Unity logs the exception and
        // silently kills the whole coroutine chain -- it never rethrows to the outer coroutine.
        static bool DriveCoroutine(IEnumerator enumerator)
        {
            try
            {
                while (enumerator.MoveNext())
                {
                    var nested = enumerator.Current as IEnumerator;
                    if (nested != null && !DriveCoroutine(nested))
                    {
                        return false;
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        static IEnumerator CoroutineYieldingNested(IEnumerator nested)
        {
            yield return nested;
        }

        static IEnumerator OuterWithValueAfterNestedError(Exception exception)
        {
            yield return new ThrowingEnumerator(exception);
            yield return "should not be delivered";
        }

        static IEnumerator TwoThrowingEnumerators(Exception first, Exception second)
        {
            yield return new ThrowingEnumerator(first);
            yield return new ThrowingEnumerator(second);
        }

        [Test]
        public void NestedEnumeratorError_PropagatesToOnError()
        {
            var observer = new RecordingObserver<object>();
            var exception = new InvalidOperationException("nested failure");
            var wrapped = InvokeWrapEnumeratorYieldValue(CoroutineYieldingNested(new ThrowingEnumerator(exception)), observer);

            DriveCoroutine(wrapped).IsTrue(); // the wrapper itself must not let the exception escape

            observer.OnErrorCallCount.Is(1);
            observer.Error.Is(exception);
            observer.OnCompletedCallCount.Is(0);
            observer.Values.Count.Is(0);
        }

        [Test]
        public void NestedEnumeratorCompletion_DeliversOnCompletedWithoutOnNext()
        {
            var observer = new RecordingObserver<object>();
            var nested = new ValueEnumerator("a", "b");
            var wrapped = InvokeWrapEnumeratorYieldValue(CoroutineYieldingNested(nested), observer, nullAsNextUpdate: false);

            DriveCoroutine(wrapped).IsTrue();

            observer.OnErrorCallCount.Is(0);
            observer.OnCompletedCallCount.Is(1);
            // Nested enumerator content is not converted into T values -- only the outer
            // coroutine's own non-IEnumerator yields become OnNext(T).
            observer.Values.Count.Is(0);
        }

        [Test]
        public void NestedEnumeratorError_StopsOuterCoroutineFromPublishingLaterValues()
        {
            var observer = new RecordingObserver<object>();
            var exception = new InvalidOperationException("nested failure");
            var wrapped = InvokeWrapEnumeratorYieldValue<object>(OuterWithValueAfterNestedError(exception), observer);

            DriveCoroutine(wrapped).IsTrue();

            observer.OnErrorCallCount.Is(1);
            observer.OnCompletedCallCount.Is(0);
            observer.Values.Count.Is(0);
        }

        [Test]
        public void DeeplyNestedEnumeratorError_DoesNotDoubleFireOnError()
        {
            // outer -> nested (a coroutine yielding two throwing enumerators in turn):
            // once the first nested-nested enumerator errors, no further level should keep running.
            var observer = new RecordingObserver<object>();
            var first = new InvalidOperationException("first nested failure");
            var second = new InvalidOperationException("second nested failure");
            var wrapped = InvokeWrapEnumeratorYieldValue<object>(CoroutineYieldingNested(TwoThrowingEnumerators(first, second)), observer);

            DriveCoroutine(wrapped).IsTrue();

            observer.OnErrorCallCount.Is(1);
            observer.Error.Is(first);
            observer.OnCompletedCallCount.Is(0);
        }

        [Test]
        public void NestedEnumeratorError_PropagatesToOnError_ThroughWrapToCancellableEnumerator()
        {
            var observer = new RecordingObserver<object>();
            var exception = new InvalidOperationException("nested failure");
            var wrapped = InvokeWrapToCancellableEnumerator(CoroutineYieldingNested(new ThrowingEnumerator(exception)), observer);

            DriveCoroutine(wrapped).IsTrue(); // the wrapper itself must not let the exception escape

            observer.OnErrorCallCount.Is(1);
            observer.Error.Is(exception);
        }

        [Test]
        public void DeeplyNestedEnumeratorError_DoesNotDoubleFireOnError_ThroughWrapToCancellableEnumerator()
        {
            var observer = new RecordingObserver<object>();
            var first = new InvalidOperationException("first nested failure");
            var second = new InvalidOperationException("second nested failure");
            var wrapped = InvokeWrapToCancellableEnumerator<object>(CoroutineYieldingNested(TwoThrowingEnumerators(first, second)), observer);

            DriveCoroutine(wrapped).IsTrue();

            observer.OnErrorCallCount.Is(1);
            observer.Error.Is(first);
        }

        static IEnumerator CoroutineYieldingObservableYieldInstruction(Exception exception)
        {
            // ObservableYieldInstruction<T> is itself an IEnumerator (that's what lets a coroutine
            // do `yield return obs.ToYieldInstruction()`), so it takes the same nested-IEnumerator
            // path as any other nested coroutine in WrapToCancellableEnumerator.
            yield return Observable.Throw<int>(exception).ToYieldInstruction(throwOnError: true);
        }

        [Test]
        public void NestedObservableYieldInstructionError_PropagatesToOnError_ThroughWrapToCancellableEnumerator()
        {
            var observer = new RecordingObserver<object>();
            var exception = new InvalidOperationException("yield instruction failure");
            var wrapped = InvokeWrapToCancellableEnumerator(CoroutineYieldingObservableYieldInstruction(exception), observer);

            DriveCoroutine(wrapped).IsTrue(); // the wrapper itself must not let the exception escape

            observer.OnErrorCallCount.Is(1);
            observer.Error.Is(exception);
        }
    }
}
