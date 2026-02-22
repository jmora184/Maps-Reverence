using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Put this on your "Base" BoxCollider GameObject (the collider must have Is Trigger = ON).
/// When the player enters, it refills:
/// - Player health (via PlayerVitals if present)
/// - Ammo (tries to refill every WeaponAmmo found on the player's guns / gunHolder)
///
/// This is written to work even if your WeaponAmmo API changes:
/// it first tries to call common refill methods (RefillAll/Refill/FillToMax/etc),
/// and if none exist, it uses reflection to set common fields/properties to their max values.
/// </summary>
public class BaseRefillZone : MonoBehaviour
{
    [Header("Who triggers the refill")]
    [Tooltip("Optional. If set, only colliders with this tag will trigger.")]
    public string playerTag = "Player";

    [Tooltip("If true, will search up the hierarchy (recommended when the trigger hits a child collider).")]
    public bool searchInParents = true;

    [Header("What to refill")]
    public bool refillHealth = true;
    public bool refillAmmo = true;

    [Tooltip("If true, allies that enter this zone also get healed to full (no AllyController edits needed).")]
    public bool refillAllies = true;

    [Header("Behavior")]
    [Tooltip("If false, refills only once per enter. If true, will also refill again after cooldown while staying inside.")]
    public bool allowRepeatWhileInside = false;

    [Tooltip("Seconds between refills while inside (only used if Allow Repeat While Inside is true).")]
    public float repeatCooldown = 1.0f;

    [Tooltip("Log what it refilled (useful while testing).")]
    public bool debugLogs = false;

    private float _nextAllowedTime = 0f;

    private void Reset()
    {
        var bc = GetComponent<BoxCollider>();
        if (bc != null) bc.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        TryRefill(other, entering: true);
    }

    private void OnTriggerStay(Collider other)
    {
        if (!allowRepeatWhileInside) return;
        TryRefill(other, entering: false);
    }

    private void TryRefill(Collider other, bool entering)
    {
        if (Time.time < _nextAllowedTime) return;

        // ✅ Allies: heal to full if they enter the base zone (no AllyController edits required).
        // We detect allies by the presence of AllyHealth.
        if (refillAllies)
        {
            var allyHealth = searchInParents ? other.GetComponentInParent<AllyHealth>() : other.GetComponent<AllyHealth>();
            if (allyHealth != null)
            {
                // If your player also has AllyHealth (unlikely), don't treat them as an ally.
                bool isPlayerTagged = !string.IsNullOrWhiteSpace(playerTag) &&
                                      (other.CompareTag(playerTag) || (searchInParents && other.transform.root.CompareTag(playerTag)));

                if (!isPlayerTagged)
                {
                    RefillAllyHealth(allyHealth);
                    _nextAllowedTime = Time.time + Mathf.Max(0.05f, repeatCooldown);

                    if (debugLogs)
                        Debug.Log($"[BaseRefillZone] Refilled ally health for {allyHealth.gameObject.name} ({(entering ? "enter" : "stay")}).", this);

                    return; // Ally-only refill
                }
            }
        }

        var player = ResolvePlayer(other);
        if (player == null) return;

        if (refillHealth)
            RefillPlayerHealth(player);

        if (refillAmmo)
            RefillPlayerAmmo(player);

        _nextAllowedTime = Time.time + Mathf.Max(0.05f, repeatCooldown);

        if (debugLogs)
            Debug.Log($"[BaseRefillZone] Refilled {(refillHealth ? "health" : "")}{(refillHealth && refillAmmo ? " + " : "")}{(refillAmmo ? "ammo" : "")} for {player.name} ({(entering ? "enter" : "stay")}).", this);
    }

    private GameObject ResolvePlayer(Collider other)
    {
        if (!string.IsNullOrWhiteSpace(playerTag))
        {
            bool tagged = other.CompareTag(playerTag) ||
                          (searchInParents && other.transform.root.CompareTag(playerTag));
            if (!tagged) return null;
        }

        Player2Controller p2 = searchInParents ? other.GetComponentInParent<Player2Controller>() : other.GetComponent<Player2Controller>();
        if (p2 != null) return p2.gameObject;

        return searchInParents ? other.transform.root.gameObject : other.gameObject;
    }

    private void RefillPlayerHealth(GameObject player)
    {
        var vitals = player.GetComponentInParent<PlayerVitals>();
        if (vitals == null && PlayerVitals.Instance != null)
            vitals = PlayerVitals.Instance;

        if (vitals != null)
        {
            vitals.SetHealth(vitals.MaxHealth);
            return;
        }

        if (debugLogs)
            Debug.LogWarning("[BaseRefillZone] No PlayerVitals found to refill health.", this);
    }

    private void RefillAllyHealth(AllyHealth allyHealth)
    {
        if (allyHealth == null) return;

        allyHealth.currentHealth = allyHealth.maxHealth;

        // AllyHealth raises UI/logic updates inside DamageAlly(), so call it with 0 to force refresh.
        allyHealth.DamageAlly(0);
    }


    private void RefillPlayerAmmo(GameObject player)
    {
        var p2 = player.GetComponentInParent<Player2Controller>();
        if (p2 == null && Player2Controller.instance != null)
            p2 = Player2Controller.instance;

        var ammoComponents = new HashSet<MonoBehaviour>();

        if (p2 != null)
        {
            if (p2.activeWeaponAmmo != null) ammoComponents.Add(p2.activeWeaponAmmo);

            if (p2.guns != null)
            {
                foreach (var g in p2.guns)
                {
                    if (g == null) continue;

                    var wa = g.GetComponent<WeaponAmmo>();
                    if (wa != null) ammoComponents.Add(wa);

                    foreach (var childWa in g.GetComponentsInChildren<WeaponAmmo>(true))
                        ammoComponents.Add(childWa);
                }
            }

            if (p2.gunHolder != null)
            {
                foreach (var wa in p2.gunHolder.GetComponentsInChildren<WeaponAmmo>(true))
                    ammoComponents.Add(wa);
            }
        }
        else
        {
            foreach (var wa in player.GetComponentsInChildren<WeaponAmmo>(true))
                ammoComponents.Add(wa);
        }

        if (ammoComponents.Count == 0)
        {
            if (debugLogs)
                Debug.LogWarning("[BaseRefillZone] No WeaponAmmo components found to refill.", this);
            return;
        }

        int refilledCount = 0;

        foreach (var mb in ammoComponents)
        {
            if (mb == null) continue;

            bool did = TryCallCommonRefillMethods(mb);
            if (!did)
                did = TryRefillViaReflection(mb);

            if (did) refilledCount++;
        }

        if (p2 != null && p2.ammoHUD != null && p2.activeWeaponAmmo != null)
        {
            p2.ammoHUD.Hook(p2.activeWeaponAmmo);
        }

        if (debugLogs)
            Debug.Log($"[BaseRefillZone] Refilled ammo on {refilledCount}/{ammoComponents.Count} WeaponAmmo components.", this);
    }

    private bool TryCallCommonRefillMethods(MonoBehaviour ammo)
    {
        string[] methodNames =
        {
            "RefillAll",
            "Refill",
            "RefillToFull",
            "FillToMax",
            "FillAll",
            "ResetAmmo",
            "RestoreAmmo",
            "SetFullAmmo",
            "CheatFullAmmo"
        };

        var t = ammo.GetType();

        foreach (var name in methodNames)
        {
            var m0 = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (m0 != null)
            {
                m0.Invoke(ammo, null);
                return true;
            }

            var m1b = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(bool) }, null);
            if (m1b != null)
            {
                m1b.Invoke(ammo, new object[] { true });
                return true;
            }

            var m1i = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int) }, null);
            if (m1i != null)
            {
                m1i.Invoke(ammo, new object[] { int.MaxValue });
                return true;
            }
        }

        return false;
    }

    private bool TryRefillViaReflection(MonoBehaviour ammo)
    {
        string[] curMagNames = { "currentInMag", "ammoInMag", "currentMag", "currentMagazine", "magAmmo", "magazineAmmo", "roundsInMag" };
        string[] maxMagNames = { "maxInMag", "magSize", "maxMag", "maxMagazine", "magCapacity", "magazineSize", "maxRoundsInMag" };

        string[] curReserveNames = { "reserveAmmo", "totalAmmo", "ammoReserve", "ammoTotal", "carriedAmmo", "currentReserve" };
        string[] maxReserveNames = { "maxReserveAmmo", "maxTotalAmmo", "maxAmmo", "reserveMax", "totalMax", "maxCarriedAmmo" };

        var t = ammo.GetType();

        bool TryGetInt(string[] names, out int value)
        {
            foreach (var n in names)
            {
                var p = t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.PropertyType == typeof(int) && p.CanRead)
                {
                    value = (int)p.GetValue(ammo);
                    return true;
                }

                var f = t.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null && f.FieldType == typeof(int))
                {
                    value = (int)f.GetValue(ammo);
                    return true;
                }
            }

            value = 0;
            return false;
        }

        bool TrySetInt(string[] names, int value)
        {
            foreach (var n in names)
            {
                var p = t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.PropertyType == typeof(int) && p.CanWrite)
                {
                    p.SetValue(ammo, value);
                    return true;
                }

                var f = t.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null && f.FieldType == typeof(int))
                {
                    f.SetValue(ammo, value);
                    return true;
                }
            }
            return false;
        }

        bool hasMaxMag = TryGetInt(maxMagNames, out int maxMag);
        bool hasMaxRes = TryGetInt(maxReserveNames, out int maxRes);

        bool didSomething = false;

        if (hasMaxMag)
            didSomething |= TrySetInt(curMagNames, maxMag);

        if (hasMaxRes)
            didSomething |= TrySetInt(curReserveNames, maxRes);

        if (didSomething)
        {
            ammo.SendMessage("RefreshUI", SendMessageOptions.DontRequireReceiver);
            ammo.SendMessage("UpdateHUD", SendMessageOptions.DontRequireReceiver);
            ammo.SendMessage("PushToUI", SendMessageOptions.DontRequireReceiver);
        }

        return didSomething;
    }
}
