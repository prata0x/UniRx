using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace UniRx.Tests
{
    public class ThrottleTest
    {
        [Test]
        public void PublishesLatestValueAfterDueTimeElapsesWithNoNewValue()
        {
            var scheduler = new TestScheduler();
            var subject = new Subject<int>();
            var received = new List<int>();

            subject.Throttle(TimeSpan.FromTicks(100), scheduler).Subscribe(received.Add);

            subject.OnNext(1);
            scheduler.AdvanceBy(100);

            received.Is(1);
        }

        [Test]
        public void NewValueBeforeDueTimeCancelsThePendingPublish()
        {
            var scheduler = new TestScheduler();
            var subject = new Subject<int>();
            var received = new List<int>();

            subject.Throttle(TimeSpan.FromTicks(100), scheduler).Subscribe(received.Add);

            subject.OnNext(1);
            scheduler.AdvanceBy(50);
            subject.OnNext(2);
            scheduler.AdvanceBy(50); // only 50 ticks since the second value; its own publish isn't due yet

            received.Is(2);
        }

        [Test]
        public void OnCompleted_PublishesLatestPendingValue()
        {
            var scheduler = new TestScheduler();
            var subject = new Subject<int>();
            var received = new List<int>();
            var completed = false;

            subject.Throttle(TimeSpan.FromTicks(100), scheduler).Subscribe(received.Add, () => completed = true);

            subject.OnNext(1);
            subject.OnCompleted();
            scheduler.AdvanceBy(100);

            received.Is(1);
            completed.IsTrue();
        }

        [Test]
        public void OnError_PropagatesAndDropsPendingValue()
        {
            var scheduler = new TestScheduler();
            var subject = new Subject<int>();
            var received = new List<int>();
            Exception caught = null;

            subject.Throttle(TimeSpan.FromTicks(100), scheduler).Subscribe(received.Add, ex => caught = ex);

            subject.OnNext(1);
            var exception = new InvalidOperationException("test");
            subject.OnError(exception);
            scheduler.AdvanceBy(100);

            received.Count.Is(0);
            caught.Is(exception);
        }

        // Simulates the race where a pending publish's scheduled work has already started
        // running by the time a new value tries to cancel it (reachable in practice via
        // Scheduler.ThreadPool, where cancellation and dispatch can interleave). This fake
        // scheduler ignores Dispose() entirely -- the stale publish is guaranteed to run
        // regardless of cancellation -- so the only thing that can stop it from double-publishing
        // is the operator's own generation guard.
        class NonCancelableScheduler : IScheduler
        {
            public List<Action> Scheduled = new List<Action>();

            public DateTimeOffset Now { get { return DateTimeOffset.UtcNow; } }

            public IDisposable Schedule(Action action)
            {
                action();
                return Disposable.Empty;
            }

            public IDisposable Schedule(TimeSpan dueTime, Action action)
            {
                Scheduled.Add(action);
                return Disposable.Empty;
            }

            public void RunAllPending()
            {
                var toRun = Scheduled;
                Scheduled = new List<Action>();
                foreach (var action in toRun)
                {
                    action();
                }
            }
        }

        [Test]
        public void StalePublishDoesNotDoublePublish_EvenWhenCancellationFailsToTakeEffect()
        {
            var scheduler = new NonCancelableScheduler();
            var subject = new Subject<int>();
            var received = new List<int>();

            subject.Throttle(TimeSpan.FromSeconds(1), scheduler).Subscribe(received.Add);

            subject.OnNext(1); // schedules a publish that this fake scheduler will never actually cancel
            subject.OnNext(2); // schedules a second publish; the first one is now stale

            scheduler.RunAllPending(); // runs both the stale and the current publish callbacks

            received.Is(2);
        }

        [Test]
        public void StalePublishDoesNotPublishAfterSubscriberUnsubscribes_EvenWhenCancellationFailsToTakeEffect()
        {
            var scheduler = new NonCancelableScheduler();
            var subject = new Subject<int>();
            var received = new List<int>();

            var subscription = subject.Throttle(TimeSpan.FromSeconds(1), scheduler).Subscribe(received.Add);

            subject.OnNext(1); // schedules a publish that this fake scheduler will never actually cancel
            subscription.Dispose(); // subscriber unsubscribes before the scheduled publish runs

            scheduler.RunAllPending(); // the stale publish callback still runs despite cancellation failing

            received.Count.Is(0); // no value delivered after unsubscribe
        }
    }
}
