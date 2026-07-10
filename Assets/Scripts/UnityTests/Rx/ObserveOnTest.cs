using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;

namespace UniRx.Tests
{
    public class ObserveOnTest
    {
        [Test]
        public void ImmediateReentrantOnNext_DoesNotRecurseForever()
        {
            const int count = 50;

            var subject = new Subject<int>();
            var received = new List<int>();

            subject.ObserveOn(Scheduler.Immediate).Subscribe(x =>
            {
                received.Add(x);
                if (x < count)
                {
                    // Re-enters OnNext synchronously from within the observer's own OnNext,
                    // which used to reschedule the in-flight action forever under Scheduler.Immediate.
                    subject.OnNext(x + 1);
                }
            });

            subject.OnNext(1);

            received.Is(Enumerable.Range(1, count));
        }

        [Test]
        public void ImmediateDispose_StopsFurtherNotifications()
        {
            var subject = new Subject<int>();
            var received = new List<int>();
            var subscription = subject.ObserveOn(Scheduler.Immediate).Subscribe(received.Add);

            subject.OnNext(1);
            subscription.Dispose();
            subject.OnNext(2);

            received.Is(1);
        }

        [Test]
        public void ThreadPoolNotifications_AreDeliveredSeriallyInOrder()
        {
            const int count = 200;

            var executing = 0;
            var overlapDetected = false;
            var received = new List<int>();
            var gate = new object();
            var done = new ManualResetEvent(false);

            Observable.Range(1, count)
                .ObserveOn(Scheduler.ThreadPool)
                .Subscribe(x =>
                {
                    if (Interlocked.Increment(ref executing) > 1) overlapDetected = true;
                    try
                    {
                        Thread.Sleep(1); // widen the window so any concurrent delivery would be caught
                        lock (gate) { received.Add(x); }
                    }
                    finally
                    {
                        Interlocked.Decrement(ref executing);
                    }
                }, () => done.Set());

            done.WaitOne(TimeSpan.FromSeconds(30)).IsTrue();

            overlapDetected.IsFalse();
            received.Is(Enumerable.Range(1, count));
        }
    }
}
