using System;
using System.Reflection;
using UnityEngine;

/// <summary>
/// If the player tries to fire while the CURRENT weapon is empty,
/// automatically requests a reload again.
/// 
/// This is useful when a reload was interrupted by switching weapons:
/// - switch away mid-reload
/// - switch back to rifle with 0 in mag
/// - click fire
/// - reload starts again automatically
/// 
/// Attach to the Player (or a player manager object).
/// </summary>
public class PlayerAutoReloadOnEmpty : MonoBehaviour
{
    [Header("Input")]
    [Tooltip("If true, left mouse button can trigger auto-reload when the current weapon is empty.")]
    public bool useMouseButton0 = true;

    [Tooltip("Optional extra fire button name (old Input Manager). Leave blank if unused.")]
    public string fireButton = "Fire1";

    [Header("Behavior")]
    [Tooltip("Minimum time between auto-reload requests.")]
    public float autoReloadCooldown = 0.15f;

    [Tooltip("If true, only auto-reloads when there is reserve ammo available.")]
    public bool requireReserveAmmo = true;

    [Tooltip("Log what happens while testing.")]
    public bool debugLogs = false;

    private float _nextAllowedTime;

    void Update()
    {
        if (Time.time < _nextAllowedTime)
            return;

        if (!DidPressFireThisFrame())
            return;

        MonoBehaviour activeAmmo = FindActiveWeaponAmmo();
        if (activeAmmo == null)
            return;

        if (!IsMagazineEmpty(activeAmmo))
            return;

        if (requireReserveAmmo && !HasReserveAmmo(activeAmmo))
        {
            if (debugLogs)
                Debug.Log("[PlayerAutoReloadOnEmpty] Current weapon is empty, but there is no reserve ammo.");
            return;
        }

        bool requestedAmmoReload = RequestAmmoReload(activeAmmo);
        bool requestedAnimReload = RequestReloadAnimation();

        if (requestedAmmoReload || requestedAnimReload)
        {
            _nextAllowedTime = Time.time + Mathf.Max(0.01f, autoReloadCooldown);

            if (debugLogs)
                Debug.Log($"[PlayerAutoReloadOnEmpty] Auto-reload requested. ammoReload={requestedAmmoReload}, animReload={requestedAnimReload}");
        }
    }

    private bool DidPressFireThisFrame()
    {
        bool pressed = false;

        if (useMouseButton0 && Input.GetMouseButtonDown(0))
            pressed = true;

        if (!string.IsNullOrWhiteSpace(fireButton))
        {
            try
            {
                if (Input.GetButtonDown(fireButton))
                    pressed = true;
            }
            catch
            {
                // Ignore if the input axis/button is not defined.
            }
        }

        return pressed;
    }

    private MonoBehaviour FindActiveWeaponAmmo()
    {
        // Best case: use Player2Controller.activeWeaponAmmo if your project exposes it.
        Player2Controller p2 = Player2Controller.instance;
        if (p2 == null)
            p2 = GetComponentInParent<Player2Controller>();

        if (p2 != null)
        {
            var t = p2.GetType();

            var activeAmmoProp = t.GetProperty("activeWeaponAmmo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (activeAmmoProp != null)
            {
                object value = activeAmmoProp.GetValue(p2);
                if (value is MonoBehaviour mb1) return mb1;
            }

            var activeAmmoField = t.GetField("activeWeaponAmmo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (activeAmmoField != null)
            {
                object value = activeAmmoField.GetValue(p2);
                if (value is MonoBehaviour mb2) return mb2;
            }

            if (p2.gunHolder != null)
            {
                var allAmmo = p2.gunHolder.GetComponentsInChildren<MonoBehaviour>(true);
                for (int i = 0; i < allAmmo.Length; i++)
                {
                    MonoBehaviour mb = allAmmo[i];
                    if (mb == null) continue;
                    if (!LooksLikeWeaponAmmo(mb)) continue;
                    if (!mb.gameObject.activeInHierarchy) continue;
                    return mb;
                }
            }
        }

        // Fallback: scan this hierarchy for an active WeaponAmmo-like script.
        MonoBehaviour[] monos = GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < monos.Length; i++)
        {
            MonoBehaviour mb = monos[i];
            if (mb == null) continue;
            if (!LooksLikeWeaponAmmo(mb)) continue;
            if (!mb.gameObject.activeInHierarchy) continue;
            return mb;
        }

        return null;
    }

    private bool LooksLikeWeaponAmmo(MonoBehaviour mb)
    {
        if (mb == null) return false;
        string typeName = mb.GetType().Name;
        return string.Equals(typeName, "WeaponAmmo", StringComparison.OrdinalIgnoreCase) ||
               typeName.IndexOf("ammo", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private bool IsMagazineEmpty(MonoBehaviour ammo)
    {
        return TryGetInt(ammo, new[]
        {
            "currentInMag","ammoInMag","currentMag","currentMagazine","magAmmo","magazineAmmo","roundsInMag",
            "currentAmmoInMag","ammoInClip","clipAmmo","bulletsInClip","bulletsInMag","currentClip","curClip","curMag"
        }, out int currentMag) && currentMag <= 0;
    }

    private bool HasReserveAmmo(MonoBehaviour ammo)
    {
        if (!TryGetInt(ammo, new[]
        {
            "reserveAmmo","totalAmmo","ammoReserve","ammoTotal","carriedAmmo","currentReserve",
            "remainingAmmo","ammoLeft","bulletsLeft","totalBullets","currentTotalAmmo","reserve","spareAmmo"
        }, out int reserve))
        {
            // If we can't read reserve ammo, allow reload request anyway.
            return true;
        }

        return reserve > 0;
    }

    private bool RequestAmmoReload(MonoBehaviour ammo)
    {
        if (ammo == null) return false;

        string[] methodNames =
        {
            "TryReload",
            "RequestReload",
            "Reload",
            "StartReload",
            "BeginReload"
        };

        Type t = ammo.GetType();

        for (int i = 0; i < methodNames.Length; i++)
        {
            MethodInfo m0 = t.GetMethod(methodNames[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (m0 != null)
            {
                m0.Invoke(ammo, null);
                return true;
            }
        }

        // Fallback: SendMessage in case the method exists but reflection did not find a public signature we expected.
        ammo.SendMessage("TryReload", SendMessageOptions.DontRequireReceiver);
        ammo.SendMessage("RequestReload", SendMessageOptions.DontRequireReceiver);
        ammo.SendMessage("Reload", SendMessageOptions.DontRequireReceiver);
        ammo.SendMessage("StartReload", SendMessageOptions.DontRequireReceiver);
        ammo.SendMessage("BeginReload", SendMessageOptions.DontRequireReceiver);
        return true;
    }

    private bool RequestReloadAnimation()
    {
        PlayerWeaponReloadAnimation reloadAnim = FindActiveReloadAnimation();
        if (reloadAnim == null)
            return false;

        reloadAnim.RequestReload();
        return true;
    }

    private PlayerWeaponReloadAnimation FindActiveReloadAnimation()
    {
        Player2Controller p2 = Player2Controller.instance;
        if (p2 == null)
            p2 = GetComponentInParent<Player2Controller>();

        if (p2 != null && p2.gunHolder != null)
        {
            PlayerWeaponReloadAnimation[] anims = p2.gunHolder.GetComponentsInChildren<PlayerWeaponReloadAnimation>(true);
            for (int i = 0; i < anims.Length; i++)
            {
                if (anims[i] != null && anims[i].gameObject.activeInHierarchy)
                    return anims[i];
            }
        }

        PlayerWeaponReloadAnimation[] fallback = GetComponentsInChildren<PlayerWeaponReloadAnimation>(true);
        for (int i = 0; i < fallback.Length; i++)
        {
            if (fallback[i] != null && fallback[i].gameObject.activeInHierarchy)
                return fallback[i];
        }

        return null;
    }

    private bool TryGetInt(MonoBehaviour target, string[] names, out int value)
    {
        value = 0;
        if (target == null) return false;

        Type t = target.GetType();

        for (int i = 0; i < names.Length; i++)
        {
            string n = names[i];

            PropertyInfo p = t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && p.PropertyType == typeof(int) && p.CanRead)
            {
                value = (int)p.GetValue(target);
                return true;
            }

            FieldInfo f = t.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null && f.FieldType == typeof(int))
            {
                value = (int)f.GetValue(target);
                return true;
            }
        }

        return false;
    }
}
