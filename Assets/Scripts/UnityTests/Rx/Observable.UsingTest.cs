using System;
using NUnit.Framework;

namespace UniRx.Tests
{
    public class ObservableUsingTest
    {
        class TrackingResource : IDisposable
        {
            public int DisposeCallCount { get; private set; }

            public void Dispose()
            {
                DisposeCallCount++;
            }
        }

        [Test]
        public void DeliversValuesAndDisposesResourceOnCompletion()
        {
            var resource = new TrackingResource();

            var result = Observable.Using(() => resource, r => Observable.Range(1, 3))
                .ToArray()
                .Wait();

            result.Is(1, 2, 3);
            resource.DisposeCallCount.Is(1);
        }

        [Test]
        public void DisposesResourceWhenSubscriptionIsDisposedEarly()
        {
            var resource = new TrackingResource();
            var subject = new Subject<int>();

            var subscription = Observable.Using(() => resource, r => subject).Subscribe();

            resource.DisposeCallCount.Is(0);

            subscription.Dispose();

            resource.DisposeCallCount.Is(1);
        }

        [Test]
        public void ResourceFactoryException_PropagatesToOnErrorWithoutCreatingResource()
        {
            var exception = new InvalidOperationException("resourceFactory failed");

            Assert.Throws<InvalidOperationException>(() =>
                Observable.Using<int, TrackingResource>(
                    () => { throw exception; },
                    r => Observable.Return(1))
                .Wait());
        }

        [Test]
        public void ObservableFactoryException_DisposesResourceAndPropagatesToOnError()
        {
            var resource = new TrackingResource();
            var exception = new InvalidOperationException("observableFactory failed");

            Assert.Throws<InvalidOperationException>(() =>
                Observable.Using<int, TrackingResource>(
                    () => resource,
                    r => { throw exception; })
                .Wait());

            resource.DisposeCallCount.Is(1);
        }

        [Test]
        public void DisposesResourceWhenSynchronousSubscribeConsumerThrows()
        {
            var resource = new TrackingResource();
            var exception = new InvalidOperationException("consumer failed");

            Assert.Throws<InvalidOperationException>(() =>
                Observable.Using(() => resource, r => Observable.Return(1))
                    .Subscribe(_ => { throw exception; }));

            resource.DisposeCallCount.Is(1);
        }
    }
}
