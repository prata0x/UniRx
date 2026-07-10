using System;
using NUnit.Framework;

namespace UniRx.Tests
{
    public class ObservableToAsyncTest
    {
        [Test]
        public void ToAsyncFunc2()
        {
            Func<int, int, int> add = (x, y) => x + y;
            Observable.ToAsync(add)(1, 2).Wait().Is(3);
        }

        [Test]
        public void ToAsyncFunc3()
        {
            Func<int, int, int, int> add = (x, y, z) => x + y + z;
            Observable.ToAsync(add)(1, 2, 3).Wait().Is(6);
        }

        [Test]
        public void ToAsyncFunc4()
        {
            Func<int, int, int, int, int> add = (x, y, z, w) => x + y + z + w;
            Observable.ToAsync(add)(1, 2, 3, 4).Wait().Is(10);
        }

        [Test]
        public void ToAsyncFuncPropagatesException()
        {
            Func<int, int, int> throwing = (x, y) => { throw new InvalidOperationException("fail"); };
            Assert.Throws<InvalidOperationException>(() => Observable.ToAsync(throwing)(1, 2).Wait());
        }

        [Test]
        public void ToAsyncAction2()
        {
            var received = default(Tuple<int, int>);
            Action<int, int> record = (x, y) => received = Tuple.Create(x, y);

            Observable.ToAsync(record)(1, 2).Wait();

            received.Item1.Is(1);
            received.Item2.Is(2);
        }

        [Test]
        public void ToAsyncAction3()
        {
            var received = default(Tuple<int, int, int>);
            Action<int, int, int> record = (x, y, z) => received = Tuple.Create(x, y, z);

            Observable.ToAsync(record)(1, 2, 3).Wait();

            received.Item1.Is(1);
            received.Item2.Is(2);
            received.Item3.Is(3);
        }

        [Test]
        public void ToAsyncAction4()
        {
            var received = default(Tuple<int, int, int, int>);
            Action<int, int, int, int> record = (x, y, z, w) => received = Tuple.Create(x, y, z, w);

            Observable.ToAsync(record)(1, 2, 3, 4).Wait();

            received.Item1.Is(1);
            received.Item2.Is(2);
            received.Item3.Is(3);
            received.Item4.Is(4);
        }

        [Test]
        public void ToAsyncActionPropagatesException()
        {
            Action<int, int> throwing = (x, y) => { throw new InvalidOperationException("fail"); };
            Assert.Throws<InvalidOperationException>(() => Observable.ToAsync(throwing)(1, 2).Wait());
        }
    }
}
