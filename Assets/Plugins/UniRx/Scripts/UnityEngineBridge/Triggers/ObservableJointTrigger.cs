#if !UNITY_2019_1_OR_NEWER || UNIRX_PHYSICS_SUPPORT || UNIRX_PHYSICS2D_SUPPORT

using System; // require keep for Windows Universal App
using UnityEngine;

namespace UniRx.Triggers
{
    [DisallowMultipleComponent]
    public class ObservableJointTrigger : ObservableTriggerBase
    {
#if !UNITY_2019_1_OR_NEWER || UNIRX_PHYSICS_SUPPORT
        Subject<float> onJointBreak;

        void OnJointBreak(float breakForce)
        {
            if (onJointBreak != null) onJointBreak.OnNext(breakForce);
        }

        public IObservable<float> OnJointBreakAsObservable()
        {
            return onJointBreak ?? (onJointBreak = new Subject<float>());
        }
#endif

#if !UNITY_2019_1_OR_NEWER || UNIRX_PHYSICS2D_SUPPORT
        Subject<Joint2D> onJointBreak2D;

        void OnJointBreak2D(Joint2D brokenJoint)
        {
            if (onJointBreak2D != null) onJointBreak2D.OnNext(brokenJoint);
        }

        public IObservable<Joint2D> OnJointBreak2DAsObservable()
        {
            return onJointBreak2D ?? (onJointBreak2D = new Subject<Joint2D>());
        }
#endif

        protected override void RaiseOnCompletedOnDestroy()
        {
#if !UNITY_2019_1_OR_NEWER || UNIRX_PHYSICS_SUPPORT
            if (onJointBreak != null)
            {
                onJointBreak.OnCompleted();
            }
#endif

#if !UNITY_2019_1_OR_NEWER || UNIRX_PHYSICS2D_SUPPORT
            if (onJointBreak2D != null)
            {
                onJointBreak2D.OnCompleted();
            }
#endif
        }
    }
}

#endif
