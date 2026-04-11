using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Put this on your "Base" BoxCollider GameObject (the collider must have Is Trigger = ON).
/// When something enters:
/// - Player: refills health + ammo + grenades
/// - Allies: refills health (no AllyController edits required)
/// 
/// IMPORTANT (Unity trigger rule):
/// OnTriggerEnter/Stay requires at least ONE of the two colliders to have a Rigidbody (can be kinematic),
/// OR a CharacterController. If your allies have only colliders + NavMeshAgent, add a kinematic Rigidbody to ally root.
/// </summary>
public class BaseRefillZone : MonoBehaviour
{
    [Header("Activation Gate")]
    [Tooltip("If enabled, this refill zone only works after the linked/parent base has been activated.")]
    public bool requireBaseActivation = true;

    [Tooltip("Optional explicit reference. If left empty, the script will try to find a BaseActivator on this object or in parents.")]
    public BaseActivator linkedBaseActivator;

    [Tooltip("If enabled, refill is blocked while the linked/parent base detects enemies in the area.")]
    public bool blockRefillWhenEnemiesDetected = true;

    [Header("Who triggers the refill")]
    [Tooltip("Optional. If set, only colliders with this tag will trigger player refill.")]
    public string playerTag = "Player";

    [Tooltip("If true, will search up the hierarchy (recommended when the trigger hits a child collider).")]
    public bool searchInParents = true;

    [Header("What to refill")]
    public bool refillHealth = true;
    public bool refillAmmo = true;
    public bool refillGrenades = true;

    [Tooltip("If true, allies that enter this zone also get healed to full (no AllyController edits needed).")]
    public bool refillAllies = true;

    [Header("Behavior")]
    [Tooltip("If false, refills only once per enter. If true, will also refill again after cooldown while staying inside.")]
    public bool allowRepeatWhileInside = false;

    [Tooltip("Seconds between refills while inside (only used if Allow Repeat While Inside is true).")]
    public float repeatCooldown = 1.0f;

    [Tooltip("Log what it refilled (useful while testing).")]
    public bool debugLogs = false;

    [Header("Audio")]
    [Tooltip("Optional sound to play each time the player refill is triggered.")]
    public AudioClip playerRefillSfx;

    [Tooltip("Volume for the player refill sound.")]
    [Range(0f, 1f)]
    public float playerRefillSfxVolume = 1f;

    [Tooltip("Optional AudioSource override. If left empty, the script uses PlayClipAtPoint.")]
    public AudioSource playerRefillAudioSource;

    private float _nextAllowedTime = 0f;

    private BaseActivator ResolveBaseActivator()
    {
        if (linkedBaseActivator != null) return linkedBaseActivator;

        linkedBaseActivator = GetComponent<BaseActivator>();
        if (linkedBaseActivator != null) return linkedBaseActivator;

        linkedBaseActivator = GetComponentInParent<BaseActivator>();
        return linkedBaseActivator;
    }

    private bool IsBaseActivated()
    {
        if (!requireBaseActivation) return true;

        var activator = ResolveBaseActivator();
        if (activator == null)
        {
            if (debugLogs)
                Debug.LogWarning("[BaseRefillZone] Require Base Activation is enabled, but no BaseActivator was found. Refill blocked.", this);
            return false;
        }

        // Prefer a public/property route if present.
        var t = activator.GetType();
        var prop = t.GetProperty("IsActivated", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop != null && prop.PropertyType == typeof(bool) && prop.CanRead)
            return (bool)prop.GetValue(activator);

        // Fallback to the private field used in your current BaseActivator implementation.
        var field = t.GetField("hasActivated", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (field != null && field.FieldType == typeof(bool))
            return (bool)field.GetValue(activator);

        if (debugLogs)
            Debug.LogWarning("[BaseRefillZone] Found BaseActivator, but could not read activation state. Refill blocked.", this);
        return false;
    }


    private bool AreEnemiesBlockingRefill()
    {
        if (!blockRefillWhenEnemiesDetected) return false;

        var activator = ResolveBaseActivator();
        if (activator == null)
            return false;

        var t = activator.GetType();

        var prop = t.GetProperty("AreEnemiesDetectedInArea", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop != null && prop.PropertyType == typeof(bool) && prop.CanRead)
            return (bool)prop.GetValue(activator);

        var method = t.GetMethod("AreEnemiesActiveInArea", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (method != null && method.ReturnType == typeof(bool) && method.GetParameters().Length == 0)
            return (bool)method.Invoke(activator, null);

        if (debugLogs)
            Debug.LogWarning("[BaseRefillZone] Enemy blocking is enabled, but no readable enemy-detection state was found on BaseActivator. Refill will stay allowed.", this);

        return false;
    }

    private void Reset()
    {
        var bc = GetComponent<BoxCollider>();
        if (bc != null) bc.isTrigger = true;
    }

    public void RefillOccupantsImmediately()
    {
        if (!isActiveAndEnabled)
            return;

        if (!IsBaseActivated())
            return;

        if (AreEnemiesBlockingRefill())
            return;

        Collider[] hits = GetComponentsInChildren<Collider>(true);
        if (hits == null || hits.Length == 0)
            return;

        float previousNextAllowedTime = _nextAllowedTime;
        _nextAllowedTime = 0f;

        try
        {
            HashSet<Collider> processed = new HashSet<Collider>();

            for (int i = 0; i < hits.Length; i++)
            {
                Collider trigger = hits[i];
                if (trigger == null || !trigger.enabled || !trigger.isTrigger)
                    continue;

                Bounds bounds = trigger.bounds;
                if (bounds.size.sqrMagnitude <= 0f)
                    continue;

                Collider[] overlaps = Physics.OverlapBox(
                    bounds.center,
                    bounds.extents,
                    trigger.transform.rotation,
                    ~0,
                    QueryTriggerInteraction.Collide);

                for (int j = 0; j < overlaps.Length; j++)
                {
                    Collider other = overlaps[j];
                    if (other == null || other == trigger)
                        continue;

                    if (!processed.Add(other))
                        continue;

                    if (other.transform.IsChildOf(transform))
                        continue;

                    TryRefill(other, entering: true);
                    _nextAllowedTime = 0f;
                }
            }
        }
        finally
        {
            _nextAllowedTime = previousNextAllowedTime;
        }
    }

    private void OnTriggerEnter(Collider other) => TryRefill(other, entering: true);

    private void OnTriggerStay(Collider other)
    {
        if (!allowRepeatWhileInside) return;
        TryRefill(other, entering: false);
    }

    private void TryRefill(Collider other, bool entering)
    {
        if (Time.time < _nextAllowedTime) return;

        if (!IsBaseActivated())
        {
            if (debugLogs)
                Debug.Log($"[BaseRefillZone] Refill blocked because base is not activated yet. Triggered by {other.name}.", this);
            return;
        }

        if (AreEnemiesBlockingRefill())
        {
            if (debugLogs)
                Debug.Log($"[BaseRefillZone] Refill blocked because enemies are active in the area. Triggered by {other.name}.", this);
            return;
        }

        // ---- 1) Allies first (so allies don't get filtered out by playerTag) ----
        if (refillAllies)
        {
            if (TryRefillAllyHealth(other, entering))
            {
                _nextAllowedTime = Time.time + Mathf.Max(0.05f, repeatCooldown);
                return;
            }
        }

        // ---- 2) Player ----
        var player = ResolvePlayer(other);
        if (player == null) return;

        bool didAnyPlayerRefill = false;

        if (refillHealth)
        {
            RefillPlayerHealth(player);
            didAnyPlayerRefill = true;
        }

        if (refillAmmo)
        {
            RefillPlayerAmmo(player);
            didAnyPlayerRefill = true;
        }

        if (refillGrenades)
        {
            RefillPlayerGrenades(player);
            didAnyPlayerRefill = true;
        }

        if (didAnyPlayerRefill)
            PlayPlayerRefillSfx(player.transform.position);

        _nextAllowedTime = Time.time + Mathf.Max(0.05f, repeatCooldown);

        if (debugLogs)
        {
            List<string> parts = new List<string>();
            if (refillHealth) parts.Add("health");
            if (refillAmmo) parts.Add("ammo");
            if (refillGrenades) parts.Add("grenades");
            Debug.Log($"[BaseRefillZone] Refilled {string.Join(" + ", parts)} for {player.name} ({(entering ? "enter" : "stay")}).", this);
        }
    }

    private void PlayPlayerRefillSfx(Vector3 worldPosition)
    {
        if (playerRefillSfx == null) return;

        if (playerRefillAudioSource != null)
        {
            playerRefillAudioSource.PlayOneShot(playerRefillSfx, playerRefillSfxVolume);
            return;
        }

        AudioSource.PlayClipAtPoint(playerRefillSfx, worldPosition, playerRefillSfxVolume);
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

    // --------------------
    // ALLY HEALTH REFILL
    // --------------------
    private bool TryRefillAllyHealth(Collider other, bool entering)
    {
        // If this collider is the player, don't treat it as an ally.
        if (!string.IsNullOrWhiteSpace(playerTag) &&
            (other.CompareTag(playerTag) || (searchInParents && other.transform.root.CompareTag(playerTag))))
            return false;

        // Most common in your project.
        var allyHealth = searchInParents ? other.GetComponentInParent<AllyHealth>() : other.GetComponent<AllyHealth>();
        if (allyHealth != null)
        {
            allyHealth.currentHealth = allyHealth.maxHealth;

            // Some implementations ignore 0 damage; force UI refresh via messages too.
            allyHealth.DamageAlly(0);
            allyHealth.SendMessage("RefreshUI", SendMessageOptions.DontRequireReceiver);
            allyHealth.SendMessage("UpdateHealthUI", SendMessageOptions.DontRequireReceiver);
            allyHealth.SendMessage("OnHealthChanged", SendMessageOptions.DontRequireReceiver);

            if (debugLogs)
                Debug.Log($"[BaseRefillZone] Refilled AllyHealth for {allyHealth.gameObject.name} ({(entering ? "enter" : "stay")}).", this);

            return true;
        }

        // Fallback: any component on the root/parents that looks like a health component.
        var root = searchInParents ? other.transform.root : other.transform;
        var monos = root.GetComponentsInChildren<MonoBehaviour>(true);

        foreach (var mb in monos)
        {
            if (mb == null) continue;

            if (TryRefillHealthViaReflection(mb))
            {
                if (debugLogs)
                    Debug.Log($"[BaseRefillZone] Refilled health via reflection on {mb.gameObject.name} ({mb.GetType().Name}).", this);
                return true;
            }
        }

        if (debugLogs)
        {
            // Help diagnose why ally doesn't trigger at all (common: no Rigidbody/CharacterController)
            bool hasRb = root.GetComponentInChildren<Rigidbody>() != null;
            bool hasCC = root.GetComponentInChildren<CharacterController>() != null;
            if (!hasRb && !hasCC)
                Debug.LogWarning($"[BaseRefillZone] '{root.name}' entered, but likely won't trigger reliably: no Rigidbody/CharacterController found. Add a kinematic Rigidbody to ally root.", this);
        }

        return false;
    }

    private bool TryRefillHealthViaReflection(MonoBehaviour mb)
    {
        // Quick reject: avoid touching clearly unrelated scripts to reduce risk.
        var typeName = mb.GetType().Name.ToLowerInvariant();
        if (typeName.Contains("ammo") || typeName.Contains("weapon")) return false;

        string[] curNames =
        {
            "currentHealth","health","hp","curHealth","currentHP","currentHp"
        };

        string[] maxNames =
        {
            "maxHealth","maxHP","maxHp","hpMax","maximumHealth","max"
        };

        var t = mb.GetType();

        bool TryGetInt(string[] names, out int value)
        {
            foreach (var n in names)
            {
                var p = t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.PropertyType == typeof(int) && p.CanRead)
                {
                    value = (int)p.GetValue(mb);
                    return true;
                }
                var f = t.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null && f.FieldType == typeof(int))
                {
                    value = (int)f.GetValue(mb);
                    return true;
                }
            }
            value = 0;
            return false;
        }

        bool TryGetFloat(string[] names, out float value)
        {
            foreach (var n in names)
            {
                var p = t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.PropertyType == typeof(float) && p.CanRead)
                {
                    value = (float)p.GetValue(mb);
                    return true;
                }
                var f = t.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null && f.FieldType == typeof(float))
                {
                    value = (float)f.GetValue(mb);
                    return true;
                }
            }
            value = 0f;
            return false;
        }

        bool TrySetInt(string[] names, int value)
        {
            foreach (var n in names)
            {
                var p = t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.PropertyType == typeof(int) && p.CanWrite)
                {
                    p.SetValue(mb, value);
                    return true;
                }
                var f = t.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null && f.FieldType == typeof(int))
                {
                    f.SetValue(mb, value);
                    return true;
                }
            }
            return false;
        }

        bool TrySetFloat(string[] names, float value)
        {
            foreach (var n in names)
            {
                var p = t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.PropertyType == typeof(float) && p.CanWrite)
                {
                    p.SetValue(mb, value);
                    return true;
                }
                var f = t.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null && f.FieldType == typeof(float))
                {
                    f.SetValue(mb, value);
                    return true;
                }
            }
            return false;
        }

        // If there's an obvious "HealToFull/Refill/Restore" method, call it.
        string[] methodNames = { "HealToFull", "RefillHealth", "RestoreHealth", "ResetHealth", "SetFullHealth" };
        foreach (var mName in methodNames)
        {
            var m0 = t.GetMethod(mName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (m0 != null)
            {
                m0.Invoke(mb, null);
                mb.SendMessage("UpdateHealthUI", SendMessageOptions.DontRequireReceiver);
                mb.SendMessage("OnHealthChanged", SendMessageOptions.DontRequireReceiver);
                return true;
            }
        }

        // Int-based health
        if (TryGetInt(maxNames, out int maxI) && maxI > 0)
        {
            if (TrySetInt(curNames, maxI))
            {
                mb.SendMessage("UpdateHealthUI", SendMessageOptions.DontRequireReceiver);
                mb.SendMessage("OnHealthChanged", SendMessageOptions.DontRequireReceiver);
                return true;
            }
        }

        // Float-based health
        if (TryGetFloat(maxNames, out float maxF) && maxF > 0f)
        {
            if (TrySetFloat(curNames, maxF))
            {
                mb.SendMessage("UpdateHealthUI", SendMessageOptions.DontRequireReceiver);
                mb.SendMessage("OnHealthChanged", SendMessageOptions.DontRequireReceiver);
                return true;
            }
        }

        return false;
    }

    // --------------------
    // PLAYER HEALTH REFILL
    // --------------------
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

    // --------------------
    // PLAYER AMMO REFILL
    // --------------------
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
                Debug.LogWarning("[BaseRefillZone] No WeaponAmmo components found to refill. (Check your gun prefabs for WeaponAmmo.)", this);
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
            p2.ammoHUD.Hook(p2.activeWeaponAmmo);

        if (debugLogs)
            Debug.Log($"[BaseRefillZone] Refilled ammo on {refilledCount}/{ammoComponents.Count} WeaponAmmo components.", this);
    }

    // --------------------
    // PLAYER GRENADE REFILL
    // --------------------
    private void RefillPlayerGrenades(GameObject player)
    {
        var thrower = player.GetComponentInParent<PlayerThrowGrenadeOnG_Aim>();
        if (thrower == null)
            thrower = player.GetComponentInChildren<PlayerThrowGrenadeOnG_Aim>(true);

        if (thrower != null)
        {
            thrower.RefillGrenadesToFull();

            if (debugLogs)
                Debug.Log($"[BaseRefillZone] Refilled grenades on {thrower.gameObject.name} to full.", this);
            return;
        }

        // Fallback support in case the throw script moves or gets renamed later.
        var monos = player.GetComponentsInChildren<MonoBehaviour>(true);
        foreach (var mb in monos)
        {
            if (mb == null) continue;
            if (TryRefillGrenadesViaReflection(mb))
            {
                if (debugLogs)
                    Debug.Log($"[BaseRefillZone] Refilled grenades via reflection on {mb.gameObject.name} ({mb.GetType().Name}).", this);
                return;
            }
        }

        if (debugLogs)
            Debug.LogWarning("[BaseRefillZone] No grenade throw script found to refill grenades.", this);
    }

    private bool TryRefillGrenadesViaReflection(MonoBehaviour mb)
    {
        var typeName = mb.GetType().Name.ToLowerInvariant();
        if (!typeName.Contains("grenade")) return false;

        var t = mb.GetType();

        var refillMethod = t.GetMethod("RefillGrenadesToFull", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        if (refillMethod != null)
        {
            refillMethod.Invoke(mb, null);
            return true;
        }

        string[] currentNames = { "currentGrenades", "grenadeCount", "currentGrenadeCount" };
        string[] maxNames = { "maxGrenades", "startingGrenades", "maxGrenadeCount" };

        bool TryGetInt(string[] names, out int value)
        {
            foreach (var n in names)
            {
                var p = t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.PropertyType == typeof(int) && p.CanRead)
                {
                    value = (int)p.GetValue(mb);
                    return true;
                }

                var f = t.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null && f.FieldType == typeof(int))
                {
                    value = (int)f.GetValue(mb);
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
                    p.SetValue(mb, value);
                    return true;
                }

                var f = t.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null && f.FieldType == typeof(int))
                {
                    f.SetValue(mb, value);
                    return true;
                }
            }
            return false;
        }

        if (TryGetInt(maxNames, out int maxGrenades) && maxGrenades > 0)
        {
            if (TrySetInt(currentNames, maxGrenades))
                return true;
        }

        return false;
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
            "ResetAmmoToDefaults",
            "RestoreAmmo",
            "SetFullAmmo",
            "CheatFullAmmo",
            "AddAmmo",
            "GiveAmmo",
            "SetAmmo",
            "ReloadFull"
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

            var m2i = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int), typeof(int) }, null);
            if (m2i != null)
            {
                m2i.Invoke(ammo, new object[] { int.MaxValue, int.MaxValue });
                return true;
            }
        }

        return false;
    }

    private bool TryRefillViaReflection(MonoBehaviour ammo)
    {
        string[] curMagNames =
        {
            "currentInMag","ammoInMag","currentMag","currentMagazine","magAmmo","magazineAmmo","roundsInMag",
            "currentAmmoInMag","ammoInClip","clipAmmo","bulletsInClip","bulletsInMag","currentClip","curClip","curMag"
        };

        string[] maxMagNames =
        {
            "maxInMag","magSize","maxMag","maxMagazine","magCapacity","magazineSize","maxRoundsInMag",
            "maxAmmoInMag","clipSize","maxClip","clipCapacity","ammoPerClip","maxClipAmmo","maxBulletsInClip"
        };

        string[] curReserveNames =
        {
            "reserveAmmo","totalAmmo","ammoReserve","ammoTotal","carriedAmmo","currentReserve",
            "remainingAmmo","ammoLeft","bulletsLeft","totalBullets","currentTotalAmmo","reserve","spareAmmo"
        };

        string[] maxReserveNames =
        {
            "maxReserveAmmo","maxTotalAmmo","maxAmmo","reserveMax","totalMax","maxCarriedAmmo",
            "maxReserve","maxTotal","maxBullets","maxTotalBullets","startingAmmo","startingTotalAmmo"
        };

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

        if (hasMaxMag) didSomething |= TrySetInt(curMagNames, maxMag);
        if (hasMaxRes) didSomething |= TrySetInt(curReserveNames, maxRes);

        if (didSomething)
        {
            ammo.SendMessage("RefreshUI", SendMessageOptions.DontRequireReceiver);
            ammo.SendMessage("UpdateHUD", SendMessageOptions.DontRequireReceiver);
            ammo.SendMessage("UpdateAmmoUI", SendMessageOptions.DontRequireReceiver);
            ammo.SendMessage("OnAmmoChanged", SendMessageOptions.DontRequireReceiver);
        }

        return didSomething;
    }
}
