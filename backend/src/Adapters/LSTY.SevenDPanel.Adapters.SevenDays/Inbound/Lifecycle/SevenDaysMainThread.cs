using System;
using System.Collections;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Inbound.Lifecycle
{
    public static class SevenDaysMainThread
    {
        public static void StartCoroutine(IEnumerator routine)
        {
            if (routine == null) throw new ArgumentNullException(nameof(routine));
            ThreadManager.StartCoroutine(routine);
        }
    }
}
