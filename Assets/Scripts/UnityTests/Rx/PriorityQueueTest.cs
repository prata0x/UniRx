using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace UniRx.Tests
{
    // PriorityQueue<T> is internal to the UniRx assembly, so it's driven here via reflection,
    // the same way Observable.FromCoroutineValueTest reaches other internal members.
    public class PriorityQueueTest
    {
        static object CreatePriorityQueue(int capacity)
        {
            var openType = typeof(TestScheduler).Assembly.GetType("UniRx.InternalUtil.PriorityQueue`1");
            var closedType = openType.MakeGenericType(typeof(int));
            return Activator.CreateInstance(closedType, capacity);
        }

        static void Enqueue(object queue, int value)
        {
            queue.GetType().GetMethod("Enqueue").Invoke(queue, new object[] { value });
        }

        static bool Remove(object queue, int value)
        {
            return (bool)queue.GetType().GetMethod("Remove").Invoke(queue, new object[] { value });
        }

        static int Dequeue(object queue)
        {
            return (int)queue.GetType().GetMethod("Dequeue").Invoke(queue, null);
        }

        static int Count(object queue)
        {
            return (int)queue.GetType().GetProperty("Count").GetValue(queue);
        }

        [Test]
        public void Remove_AtNonRootIndex_KeepsRemainingItemsInAscendingOrder()
        {
            // Regression case for a RemoveAt() that repaired the heap only from the root
            // (Heapify()) after swapping the last element into an interior slot -- the moved
            // element could end up smaller than its new parent without being percolated back up.
            var values = new[] { 10, 4, 3, 18, 1, 8, 13, 13, 20, 3, 25 };
            var queue = CreatePriorityQueue(4);
            foreach (var v in values) Enqueue(queue, v);

            // None of these sit at the heap root, forcing RemoveAt() to repair interior slots.
            foreach (var v in new[] { 8, 3, 25 }) Remove(queue, v);

            var expected = values.ToList();
            foreach (var v in new[] { 8, 3, 25 }) expected.Remove(v);
            expected.Sort();

            var actual = new List<int>();
            while (Count(queue) > 0) actual.Add(Dequeue(queue));

            CollectionAssert.AreEqual(expected, actual);
        }
    }
}
