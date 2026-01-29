using System;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Attach to a Trigger Collider (BoxCollider, MeshCollider set to Trigger, etc.)
/// to slow movement inside the volume (mud, water, snow, tall grass).
///
/// This version is intentionally GENERIC:
/// - It exposes a "Speed Multiplier" in the Inspector.
/// - It does NOT hard-reference your Player/Ally/Enemy script types,
///   so it will still compile even if you haven't imported updated controller scripts yet.
/// - It will call a method named SetWaterSlow(bool isInSlow, float multiplier)
///   on any component found on the entering object (or its parents).
///
/// To make it work, ensure your movement scripts implement:
///     public void SetWaterSlow(bool isInSlowZone, float multiplier)
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class SlowZone : MonoBehaviour
{
    [Header("Slow Settings")]
    [Tooltip("Multiplier applied to movement speed while inside this trigger (1 = normal, 0.5 = half speed).")]
    [Range(0.05f, 1f)]
    public float speedMultiplier = 0.6f;

    [Header("Targeting")]
    [Tooltip("Only affect objects on these layers (optional).")]
    public bool filterByLayer = false;

    [Tooltip("Layers affected when filterByLayer is enabled.")]
    public LayerMask affectedLayers = ~0;

    [Header("Advanced")]
    [Tooltip("Method name to invoke on movement scripts. Default matches the updated controllers I provided.")]
    public string slowMethodName = "SetWaterSlow";

    // Cache MethodInfo lookups per Type for performance.
    private static readonly System.Collections.Generic.Dictionary<Type, MethodInfo> _methodCache =
        new System.Collections.Generic.Dictionary<Type, MethodInfo>();

    private void Reset()
    {
        // Helpfully ensure the collider is set as a trigger.
        Collider c = GetComponent<Collider>();
        if (c != null) c.isTrigger = true;
    }

    private bool PassesLayerFilter(GameObject go)
    {
        if (!filterByLayer) return true;
        return ((1 << go.layer) & affectedLayers) != 0;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other == null) return;
        if (!PassesLayerFilter(other.gameObject)) return;

        InvokeSlow(other, true, speedMultiplier);
    }

    private void OnTriggerExit(Collider other)
    {
        if (other == null) return;
        if (!PassesLayerFilter(other.gameObject)) return;

        // Restore to normal (multiplier = 1)
        InvokeSlow(other, false, 1f);
    }

    private void InvokeSlow(Collider other, bool isInSlowZone, float multiplier)
    {
        // Search up the hierarchy. This supports colliders on child bones, feet, etc.
        MonoBehaviour[] behaviours = other.GetComponentsInParent<MonoBehaviour>(true);
        if (behaviours == null || behaviours.Length == 0) return;

        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour mb = behaviours[i];
            if (mb == null) continue;

            MethodInfo mi = GetCachedSlowMethod(mb.GetType());
            if (mi == null) continue;

            try
            {
                mi.Invoke(mb, new object[] { isInSlowZone, multiplier });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"SlowZone: Failed invoking {slowMethodName} on {mb.GetType().Name}: {ex.Message}", mb);
            }
        }
    }

    private MethodInfo GetCachedSlowMethod(Type t)
    {
        if (t == null) return null;

        if (_methodCache.TryGetValue(t, out MethodInfo cached))
            return cached;

        // Expect signature: void SetWaterSlow(bool, float)
        MethodInfo mi = t.GetMethod(
            slowMethodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(bool), typeof(float) },
            modifiers: null
        );

        _methodCache[t] = mi; // cache even if null
        return mi;
    }
}
