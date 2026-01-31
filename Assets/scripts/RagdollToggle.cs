using System.Collections.Generic;
using UnityEngine;

namespace MnR
{
    /// <summary>
    /// Simple ragdoll toggle:
    /// - Collects child rigidbodies + colliders (excluding the root)
    /// - When enabled: rigidbodies non-kinematic, colliders enabled, animator disabled
    /// - When disabled: rigidbodies kinematic, colliders disabled, animator enabled
    /// </summary>
    public sealed class RagdollToggle : MonoBehaviour
    {
        [Tooltip("Animator to disable when ragdoll is enabled. If null, auto-find in parents.")]
        public Animator animator;

        [Tooltip("If true, keeps trigger colliders enabled when ragdoll is disabled.")]
        public bool keepTriggerCollidersEnabled = true;

        private readonly List<Rigidbody> _rbs = new List<Rigidbody>(64);
        private readonly List<Collider> _cols = new List<Collider>(64);

        private void Awake()
        {
            if (animator == null) animator = GetComponentInParent<Animator>();

            _rbs.Clear();
            _cols.Clear();

            // Collect ragdoll bits (children only)
            var rbs = GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < rbs.Length; i++)
            {
                if (rbs[i].transform == transform) continue;
                _rbs.Add(rbs[i]);
            }

            var cols = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i].transform == transform) continue;
                _cols.Add(cols[i]);
            }

            // Default to OFF ragdoll
            EnableRagdoll(false);
        }

        public void EnableRagdoll(bool enabled)
        {
            if (animator != null) animator.enabled = !enabled;

            for (int i = 0; i < _rbs.Count; i++)
            {
                if (_rbs[i] == null) continue;
                _rbs[i].isKinematic = !enabled;
                _rbs[i].detectCollisions = enabled;
            }

            for (int i = 0; i < _cols.Count; i++)
            {
                if (_cols[i] == null) continue;

                if (!enabled && keepTriggerCollidersEnabled && _cols[i].isTrigger)
                {
                    _cols[i].enabled = true;
                    continue;
                }

                _cols[i].enabled = enabled;
            }
        }
    }
}
