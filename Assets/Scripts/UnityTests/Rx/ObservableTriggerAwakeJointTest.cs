using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UniRx.Triggers;

namespace UniRx.Tests
{
    public class ObservableAwakeTriggerTest
    {
        [Test]
        public void SubscribingAfterAwakeAlreadyRan_ReplaysImmediately()
        {
            // GetOrAddComponent(gameObject) inside AwakeAsObservable() adds the component onto an
            // already-active GameObject, so Unity calls Awake() synchronously as part of AddComponent,
            // before the subscription below ever sees the trigger's Subject.
            var go = new GameObject();
            try
            {
                var received = new List<Unit>();
                var completed = false;

                go.AwakeAsObservable().Subscribe(received.Add, () => completed = true);

                received.Count.Is(1);
                completed.IsTrue();
            }
            finally
            {
                Object.Destroy(go);
            }
        }

        [Test]
        public void InactiveGameObject_FiresOnceWhenActivated()
        {
            var go = new GameObject();
            go.SetActive(false); // Awake is deferred until the GameObject is actually activated

            try
            {
                var received = new List<Unit>();
                var completed = false;

                go.AwakeAsObservable().Subscribe(received.Add, () => completed = true);

                received.Count.Is(0);
                completed.IsFalse();

                go.SetActive(true);

                received.Count.Is(1);
                completed.IsTrue();
            }
            finally
            {
                Object.Destroy(go);
            }
        }
    }

#if !UNITY_2019_1_OR_NEWER || UNIRX_PHYSICS_SUPPORT
    public class ObservableJointTriggerTest
    {
        [Test]
        public void OnJointBreakAsObservable_AttachesTriggerComponentToJointsGameObject()
        {
            // Actually firing OnJointBreak requires stepping physics with a configured breakForce,
            // which isn't practical to pin deterministically in this test harness. This only
            // verifies the extension method's wiring: it attaches ObservableJointTrigger to the
            // Joint's GameObject and returns a subscribable observable without throwing.
            var go = new GameObject();
            try
            {
                go.AddComponent<Rigidbody>();
                var joint = go.AddComponent<HingeJoint>();

                var observable = joint.OnJointBreakAsObservable();
                var subscription = observable.Subscribe();

                go.GetComponent<ObservableJointTrigger>().IsNotNull();

                subscription.Dispose();
            }
            finally
            {
                Object.Destroy(go);
            }
        }

        [Test]
        public void OnJointBreakAsObservable_NullJoint_ReturnsEmptyWithoutThrowing()
        {
            Joint joint = null;
            var completed = false;

            joint.OnJointBreakAsObservable().Subscribe(_ => { }, () => completed = true);

            completed.IsTrue();
        }
    }
#endif
}
