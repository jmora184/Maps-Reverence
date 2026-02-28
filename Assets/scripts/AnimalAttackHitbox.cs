using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attach this to a child GameObject that has a Trigger Collider (Box/Sphere/etc).
/// The AnimalController will enable/disable this hitbox during attacks.
///
/// Important:
/// - Collider must be isTrigger = true
/// - This script should NOT be on the root; keep it on the hitbox child.
/// </summary>
[DisallowMultipleComponent]
public class AnimalAttackHitbox : MonoBehaviour
{
    [Tooltip("Owner controller. If empty, will search in parents.")]
    public AnimalController owner;

    [Tooltip("Only apply damage once per attack window to each target.")]
    public bool oneHitPerTargetPerSwing = true;

    private readonly HashSet<int> _hitIds = new HashSet<int>();
    private Collider _col;

    private void Awake()
    {
        if (!owner) owner = GetComponentInParent<AnimalController>();
        _col = GetComponent<Collider>();
        if (_col) _col.enabled = false; // start disabled; controller enables it.
    }

    public void BeginSwing()
    {
        _hitIds.Clear();
        if (_col) _col.enabled = true;
    }

    public void EndSwing()
    {
        if (_col) _col.enabled = false;
        _hitIds.Clear();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!owner || !owner.enabled) return;
        if (!owner.IsAlive) return;
        if (!owner.IsInAttackWindow) return;

        // Ignore self
        if (other.transform.IsChildOf(owner.transform)) return;

        var id = other.GetInstanceID();
        if (oneHitPerTargetPerSwing && _hitIds.Contains(id)) return;

        if (owner.TryDamageTarget(other.gameObject))
        {
            if (oneHitPerTargetPerSwing) _hitIds.Add(id);
        }
    }
}
