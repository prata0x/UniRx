#if CSHARP_7_OR_LATER || (UNITY_2018_3_OR_NEWER && (NET_STANDARD_2_0 || NET_STANDARD_2_1 || NET_4_6))
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace UniRx.Tests
{
    public class ObserverValueTupleSubscribeTest
    {
        [Test]
        public void OnNextOnly_DeliversBothElements()
        {
            var subject = new Subject<(int, string)>();
            var received = new List<(int, string)>();

            subject.Subscribe((x, y) => received.Add((x, y)));

            subject.OnNext((1, "a"));
            subject.OnNext((2, "b"));

            received.Count.Is(2);
            received[0].Is((1, "a"));
            received[1].Is((2, "b"));
        }

        [Test]
        public void OnNextWithOnError_PropagatesException()
        {
            var subject = new Subject<(int, string)>();
            Exception caught = null;

            subject.Subscribe((x, y) => { }, ex => caught = ex);

            var exception = new InvalidOperationException("test");
            subject.OnError(exception);

            caught.Is(exception);
        }

        [Test]
        public void OnNextWithOnCompleted_FiresOnCompleted()
        {
            var subject = new Subject<(int, string)>();
            var completed = false;

            subject.Subscribe((x, y) => { }, () => completed = true);

            subject.OnCompleted();

            completed.IsTrue();
        }

        [Test]
        public void OnNextWithOnErrorAndOnCompleted_DeliversAllCallbacks()
        {
            var subject = new Subject<(int, string)>();
            var received = new List<(int, string)>();
            Exception caught = null;
            var completed = false;

            subject.Subscribe((x, y) => received.Add((x, y)), ex => caught = ex, () => completed = true);

            subject.OnNext((10, "z"));
            subject.OnCompleted();

            received.Count.Is(1);
            received[0].Is((10, "z"));
            caught.IsNull();
            completed.IsTrue();
        }
    }
}
#endif
