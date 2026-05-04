using UnityEngine;
using System.Collections.Generic;

namespace AdaptiveCombatFramework {
    [CreateAssetMenu(fileName = "TelemetryEventSettings", menuName = "Telemetry/Event Settings", order = 1)]
    public class TelemetryEventSettings : ScriptableObject
    {
        [Tooltip("List of event names (as strings) that should count towards Actions Per Minute (APM).")]
        public List<string> apmEventNames = new List<string>();

        // You could add other settings here, like "events to track for total count", etc.
    }
}