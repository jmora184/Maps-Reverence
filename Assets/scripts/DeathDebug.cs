using UnityEngine;

namespace MnR
{
    /// <summary>
    /// OPTIONAL helper to test death quickly from the Inspector:
    /// - Right-click component -> "Kill Now"
    /// Or press K while selected in play mode (if enabled).
    /// </summary>
    public sealed class DeathDebug : MonoBehaviour
    {
        public DeathController death;
        public bool pressKToKill = false;

        private void Awake()
        {
            if (death == null) death = GetComponent<DeathController>();
        }

        private void Update()
        {
            if (!pressKToKill) return;
            if (Input.GetKeyDown(KeyCode.K)) KillNow();
        }

        [ContextMenu("Kill Now")]
        public void KillNow()
        {
            if (death != null) death.Die();
        }
    }
}
