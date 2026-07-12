using System;

namespace UniRx.Operators
{
    internal class ResetAfterObservable<T> : OperatorObservableBase<T>
    {
        readonly IObservable<T> source;
        readonly TimeSpan dueTime;
        readonly IScheduler scheduler;
        readonly T defaultValue;

        public ResetAfterObservable(IObservable<T> source, T defaultValue, TimeSpan dueTime, IScheduler scheduler)
            : base(scheduler == Scheduler.CurrentThread || source.IsRequiredSubscribeOnCurrentThread())
        {
            this.source = source;
            this.dueTime = dueTime;
            this.scheduler = scheduler;
            this.defaultValue = defaultValue;
        }

        protected override IDisposable SubscribeCore(IObserver<T> observer, IDisposable cancel)
        {
            return new ResetAfter(this, observer, cancel).Run();
        }

        class ResetAfter : OperatorObserverBase<T, T>
        {
            readonly ResetAfterObservable<T> parent;
            readonly object gate = new object();
            SerialDisposable cancelable;
            ulong id = 0;

            public ResetAfter(ResetAfterObservable<T> parent, IObserver<T> observer, IDisposable cancel) : base(observer, cancel)
            {
                this.parent = parent;
            }

            public IDisposable Run()
            {
                cancelable = new SerialDisposable();
                var subscription = parent.source.Subscribe(this);

                // Invalidates any reset already scheduled but not yet run when the subscriber
                // unsubscribes -- otherwise a scheduler callback that already passed its own
                // cancellation check (e.g. a ThreadPool-based scheduler) can still deliver
                // defaultValue to the observer after this subscription was disposed.
                return StableCompositeDisposable.Create(cancelable, subscription, Disposable.Create(InvalidatePendingReset));
            }

            void InvalidatePendingReset()
            {
                lock (gate)
                {
                    id = unchecked(id + 1);
                }
            }

            void OnNextReset(ulong currentid)
            {
                lock (gate)
                {
                    if (id == currentid)
                    {
                        observer.OnNext(parent.defaultValue);
                    }
                }
            }

            public override void OnNext(T value)
            {
                ulong currentid;
                lock (gate)
                {
                    observer.OnNext(value);
                    id = unchecked(id + 1);
                    currentid = id;
                }

                var d = new SingleAssignmentDisposable();
                cancelable.Disposable = d;
                d.Disposable = parent.scheduler.Schedule(parent.dueTime, () => OnNextReset(currentid));
            }

            public override void OnError(Exception error)
            {
                cancelable.Dispose();

                lock (gate)
                {
                    id = unchecked(id + 1);
                    try { observer.OnError(error); } finally { Dispose(); }
                }
            }

            public override void OnCompleted()
            {
                cancelable.Dispose();

                lock (gate)
                {
                    id = unchecked(id + 1);
                    try { observer.OnCompleted(); } finally { Dispose(); }
                }
            }
        }
    }
}
