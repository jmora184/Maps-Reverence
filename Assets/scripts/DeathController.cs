using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace MnR
{
    /// <summary>
    /// Drop-in death pipeline for allies/enemies.
    /// Call Die() when health reaches 0.
    ///
    /// Features:
    /// - Plays Animator death trigger (optional, random variant)
    /// - Disables NavMeshAgent + AI scripts
    /// - Optional ragdoll toggle
    /// - Optional sink/disable/destroy after delay (pool-friendly)
    /// </summary>
    public sealed class DeathController : MonoBehaviour
    {
        [Header("Animator")]
        [Tooltip("If null, will auto-find on this GameObject or children.")]
        public Animator animator;

        [Tooltip("Animator Trigger parameter to fire on death.")]
        public string dieTrigger = "Die";

        [Tooltip("Optional int parameter used to pick a random death animation variant (e.g., DeathIndex). Set blank to disable.")]
        public string deathIndexInt = "DeathIndex";

        [Tooltip("Inclusive min/max death index values. Used only if deathIndexInt is set.")]
        public Vector2Int deathIndexRange = new Vector2Int(0, 2);

        [Tooltip("If true, the DeathController will set Animator bool 'IsDead' = true (if it exists).")]
        public bool setIsDeadBool = true;

        [Tooltip("Animator bool name for dead state (optional).")]
        public string isDeadBool = "IsDead";

        [Tooltip("If your death clip uses root motion, enable this.")]
        public bool applyRootMotionOnDeath = false;

        [Header("Disable on death")]
        [Tooltip("Disable NavMeshAgent if present.")]
        public bool disableNavMeshAgent = true;

        [Tooltip("Disable CharacterController if present.")]
        public bool disableCharacterController = true;

        [Tooltip("Disable these components (AI / shooting / input) when dead.")]
        public List<Behaviour> disableBehaviours = new List<Behaviour>();

        [Tooltip("If true and 'Disable Behaviours' list is empty, auto-disables common AI/shooting scripts on this object and its children.")]
        public bool autoDisableAllBehavioursIfListEmpty = true;

        [Tooltip("If true, also stops any pending Coroutines/Invokes on behaviours we disable (prevents delayed shots after death).")]
        public bool stopCoroutinesAndInvokesOnDisabledBehaviours = true;

        [Tooltip("Disable these Colliders when dead (leave empty to disable all non-trigger colliders on this object).")]
        public List<Collider> disableColliders = new List<Collider>();

        [Header("Ragdoll (optional)")]
        [Tooltip("Optional ragdoll toggle component. If assigned, can switch to ragdoll on death.")]
        public RagdollToggle ragdollToggle;

        [Tooltip("If true, enable ragdoll immediately when Die() is called.")]
        public bool enableRagdollImmediately = false;

        [Tooltip("If enableRagdollImmediately is false, ragdoll will be enabled after this delay (seconds).")]
        public float ragdollDelay = 0.25f;

        [Header("Cleanup")]
        public CleanupMode cleanupMode = CleanupMode.DisableGameObject;

        [Tooltip("Delay before cleanup (seconds). For Destroy/Disable it gives the death anim time to play.")]
        public float cleanupDelay = 6f;

        [Tooltip("If CleanupMode=SinkAndDisable, the body will move down at this speed after cleanupDelay.")]
        public float sinkSpeed = 0.75f;

        [Tooltip("Seconds to sink before disabling.")]
        public float sinkDuration = 2.5f;

        public enum CleanupMode
        {
            None,
            DisableGameObject,
            DestroyGameObject,
            SinkAndDisable
        }

        public event Action OnDied;

        public bool IsDead => _isDead;

        private bool _isDead;
        private NavMeshAgent _agent;
        private CharacterController _cc;
        private float _cleanupAt;
        private float _sinkUntil;
        private Vector3 _sinkDir = Vector3.down;

        private int _dieTriggerHash;
        private int _deathIndexHash;
        private int _isDeadHash;

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>(true);
            _agent = GetComponent<NavMeshAgent>();
            _cc = GetComponent<CharacterController>();

            _dieTriggerHash = !string.IsNullOrWhiteSpace(dieTrigger) ? Animator.StringToHash(dieTrigger) : 0;
            _deathIndexHash = !string.IsNullOrWhiteSpace(deathIndexInt) ? Animator.StringToHash(deathIndexInt) : 0;
            _isDeadHash = !string.IsNullOrWhiteSpace(isDeadBool) ? Animator.StringToHash(isDeadBool) : 0;

            if (ragdollToggle == null) ragdollToggle = GetComponentInChildren<RagdollToggle>(true);
        }

        /// <summary>Call when the character should die (health <= 0).</summary>
        public void Die()
        {
            if (_isDead) return;
            _isDead = true;

            // Stop movement / AI
            if (disableNavMeshAgent && _agent != null)
            {
                _agent.isStopped = true;
                _agent.ResetPath();
                _agent.enabled = false;
            }

            if (disableCharacterController && _cc != null)
            {
                _cc.enabled = false;
            }

            // Disable chosen behaviours
            for (int i = 0; i < disableBehaviours.Count; i++)
            {
                if (disableBehaviours[i] != null) DisableBehaviourSafely(disableBehaviours[i]);
            }

            // If nothing was assigned, auto-disable typical AI/shooting/input scripts so corpses can't keep acting.
            if (autoDisableAllBehavioursIfListEmpty && (disableBehaviours == null || disableBehaviours.Count == 0))
            {
                AutoDisableAllBehaviours();
            }

            // Disable colliders
            if (disableColliders != null && disableColliders.Count > 0)
            {
                for (int i = 0; i < disableColliders.Count; i++)
                {
                    if (disableColliders[i] != null) disableColliders[i].enabled = false;
                }
            }
            else
            {
                var cols = GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < cols.Length; i++)
                {
                    // Let triggers keep working if you need corpse triggers; change if desired.
                    if (!cols[i].isTrigger) cols[i].enabled = false;
                }
            }

            // Animation
            // Ensure the death clip updates even if offscreen.
            if (animator != null) animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (animator != null)
            {
                animator.applyRootMotion = applyRootMotionOnDeath;

                if (setIsDeadBool && _isDeadHash != 0 && HasParameter(animator, _isDeadHash))
                    animator.SetBool(_isDeadHash, true);

                if (_deathIndexHash != 0 && HasParameter(animator, _deathIndexHash))
                {
                    int min = Mathf.Min(deathIndexRange.x, deathIndexRange.y);
                    int max = Mathf.Max(deathIndexRange.x, deathIndexRange.y);
                    int idx = UnityEngine.Random.Range(min, max + 1);
                    animator.SetInteger(_deathIndexHash, idx);
                }

                if (_dieTriggerHash != 0 && HasParameter(animator, _dieTriggerHash))
                    animator.SetTrigger(_dieTriggerHash);
            }

            // Ragdoll
            if (ragdollToggle != null)
            {
                if (enableRagdollImmediately)
                {
                    ragdollToggle.EnableRagdoll(true);
                }
                else if (ragdollDelay > 0f)
                {
                    Invoke(nameof(EnableRagdollDelayed), ragdollDelay);
                }
            }

            // Cleanup scheduling
            if (cleanupMode != CleanupMode.None && cleanupDelay > 0f)
                _cleanupAt = Time.time + cleanupDelay;

            OnDied?.Invoke();
        }


        private void DisableBehaviourSafely(Behaviour b)
        {
            if (b == null) return;

            // Stop delayed actions on the script so it can't fire after death.
            if (stopCoroutinesAndInvokesOnDisabledBehaviours)
            {
                if (b is MonoBehaviour mb)
                {
                    mb.CancelInvoke();
                    mb.StopAllCoroutines();
                }
            }

            b.enabled = false;
        }

        private void AutoDisableAllBehaviours()
        {
            // Disable scripts on this object and children, but keep Animator + DeathController + ragdoll helpers alive.
            var behaviours = GetComponentsInChildren<Behaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                var b = behaviours[i];
                if (b == null) continue;

                // Never disable ourselves.
                if (ReferenceEquals(b, this)) continue;

                // Keep animators running so the death animation can play.
                if (b is Animator) continue;

                // Keep ragdoll toggle (we may enable ragdoll later).
                if (b is RagdollToggle) continue;

                // Keep NavMeshAgent/CharacterController handling is already above; disabling again is fine but unnecessary.
                // Still, avoid disabling the components as "Behaviour" here since we already handled them explicitly.
                if (b is NavMeshAgent) continue;
                if (b is CharacterController) continue;

                DisableBehaviourSafely(b);
            }
        }

        private void EnableRagdollDelayed()
        {
            if (!_isDead) return;
            if (ragdollToggle != null) ragdollToggle.EnableRagdoll(true);
        }

        private void Update()
        {
            if (!_isDead) return;

            if (_cleanupAt > 0f && Time.time >= _cleanupAt)
            {
                _cleanupAt = 0f;

                switch (cleanupMode)
                {
                    case CleanupMode.DisableGameObject:
                        gameObject.SetActive(false);
                        break;

                    case CleanupMode.DestroyGameObject:
                        Destroy(gameObject);
                        break;

                    case CleanupMode.SinkAndDisable:
                        _sinkUntil = Time.time + Mathf.Max(0.1f, sinkDuration);
                        break;
                }
            }

            if (_sinkUntil > 0f)
            {
                transform.position += _sinkDir * (sinkSpeed * Time.deltaTime);
                if (Time.time >= _sinkUntil)
                {
                    _sinkUntil = 0f;
                    gameObject.SetActive(false);
                }
            }
        }

        private static bool HasParameter(Animator a, int hash)
        {
            // Animator.parameters allocates but this runs only on death, so it's fine.
            var ps = a.parameters;
            for (int i = 0; i < ps.Length; i++)
            {
                if (ps[i].nameHash == hash) return true;
            }
            return false;
        }
    }
}
