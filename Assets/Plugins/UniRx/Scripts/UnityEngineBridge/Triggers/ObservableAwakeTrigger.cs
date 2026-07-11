using System; // require keep for Windows Universal App
using UnityEngine;

namespace UniRx.Triggers
{
    [DisallowMultipleComponent]
    public class ObservableAwakeTrigger : ObservableTriggerBase
    {
        protected override void RaiseOnCompletedOnDestroy()
        {
        }
    }
}
