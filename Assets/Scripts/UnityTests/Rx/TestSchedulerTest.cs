using System;
using NUnit.Framework;

namespace UniRx.Tests
{
    public class TestSchedulerTest
    {
        int value;
        TestScheduler scheduler;

        [SetUp]
        public void Setup()
        {
            value = 0;
            scheduler = new TestScheduler();

            scheduler.Schedule(10, () => value = 1);
            scheduler.Schedule(20, () => value = 2);
            scheduler.Schedule(30, () => value = 3);
            scheduler.Schedule(30, () => value = 4);
            scheduler.Schedule(50, () => value = 5);
            scheduler.Schedule(60, () => value = 6);
        }

        [Test]
        public void InitialState()
        {
            value.Is(0);
        }

        [Test]
        public void AdvanceTo_BeforeFirstScheduledAction()
        {
            scheduler.AdvanceTo(5);
            value.Is(0);
        }

        [Test]
        public void AdvanceTo_ExactlyAtScheduledAction()
        {
            scheduler.AdvanceTo(20);
            value.Is(2);
        }

        [Test]
        public void AdvanceTo_BetweenScheduledActions()
        {
            scheduler.AdvanceTo(15);
            value.Is(1);
        }

        [Test]
        public void AdvanceTo_ExactlyAtManyScheduledActionAtSameMoment_RespectScheduledOrder()
        {
            scheduler.AdvanceTo(30);
            value.Is(4);
        }

        [Test]
        public void Start_ExecutesEverything()
        {
            scheduler.Start();
            value.Is(6);
            scheduler.Clock.Is(60);
        }

        [Test]
        public void Start_ExecutesEverythingButDoesNotExecuteSubsequentlyScheduledActionsUntilNextStart()
        {
            scheduler.Start();
            value.Is(6);

            scheduler.Schedule(70, () => value = 7);
            value.Is(6);

            scheduler.Start();
            value.Is(7);
        }

        [Test]
        public void AdvanceTo_UpdatesClock()
        {
            scheduler.AdvanceTo(35);
            scheduler.Clock.Is(35);
        }

        [Test]
        public void AdvanceTo_TimeInPast_ThrowsArgumentOutOfRangeException()
        {
            scheduler.AdvanceTo(20);
            Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.AdvanceTo(10));
        }

        [Test]
        public void AdvanceBy_RunsWorkOverRelativeSpan()
        {
            scheduler.AdvanceBy(25);
            value.Is(2);
            scheduler.Clock.Is(25);

            scheduler.AdvanceBy(10);
            value.Is(4);
            scheduler.Clock.Is(35);
        }

        [Test]
        public void Sleep_AdvancesClockWithoutRunningWork()
        {
            scheduler.Sleep(15);
            value.Is(0);
            scheduler.Clock.Is(15);

            scheduler.Start();
            value.Is(6);
        }

        [Test]
        public void AdvanceBy_MakesProgressForZeroDelayRecursiveScheduling()
        {
            // Regression case: ScheduleAbsolute must clamp a past-or-current dueTime to
            // Clock + 1 (not Clock), or work that reschedules itself with TimeSpan.Zero keeps
            // firing at the same virtual instant forever and the clock never advances.
            var localScheduler = new TestScheduler();
            var count = 0;
            const int iterations = 50;
            Action recurse = null;
            recurse = () =>
            {
                count++;
                if (count < iterations)
                {
                    localScheduler.Schedule(TimeSpan.Zero, recurse);
                }
            };
            localScheduler.Schedule(TimeSpan.Zero, recurse);

            localScheduler.AdvanceBy(iterations);

            count.Is(iterations);
            localScheduler.Clock.Is(iterations);
        }
    }
}
