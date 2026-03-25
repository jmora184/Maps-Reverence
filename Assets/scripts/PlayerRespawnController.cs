using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;

/// <summary>
/// Simple player respawn handler.
/// 
/// Put this on the Player (or a manager object), assign a respawn point,
/// and it will listen for PlayerVitals death events.
///
/// On death:
/// - waits for the respawn delay while showing an optional countdown UI
/// - teleports player to the chosen respawn point
/// - restores full health
/// - refills ammo on the player's weapons
///
/// This tries to avoid editing your existing Player2Controller / turret / base logic.
/// </summary>
public class PlayerRespawnController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerVitals playerVitals;
    [SerializeField] private Player2Controller playerController;
    [SerializeField] private CharacterController characterController;

    [Header("Respawn")]
    [Tooltip("Where the player should respawn after death or off-map recovery.")]
    [SerializeField] private Transform respawnPoint;

    [Tooltip("Delay before respawn after death. 0 = instant.")]
    [SerializeField] private float respawnDelay = 5f;

    [Tooltip("If true, match the respawn point rotation too.")]
    [SerializeField] private bool applyRespawnRotation = true;

    [Header("Respawn UI")]
    [Tooltip("Optional TMP text used to show a respawn countdown, e.g. 'Respawning in 5'.")]
    [SerializeField] private TMP_Text respawnCountdownText;

    [Tooltip("Format string used for the countdown text. {0} will be replaced by the seconds remaining.")]
    [SerializeField] private string respawnCountdownFormat = "Respawning in {0}";

    [Tooltip("Hide the countdown text object when not actively counting down.")]
    [SerializeField] private bool hideCountdownTextWhenIdle = true;


    [Header("Audio")]
    [Tooltip("Optional sound played once when the player dies and the respawn countdown begins.")]
    [SerializeField] private AudioClip deathSfx;

    [Tooltip("Optional AudioSource used to play the death sound. Leave empty to use an AudioSource on this object or a simple fallback.")]
    [SerializeField] private AudioSource deathAudioSource;

    [Tooltip("Volume used when playing the death sound.")]
    [Range(0f, 1f)]
    [SerializeField] private float deathSfxVolume = 1f;

    [Header("Off Map Recovery")]
    [Tooltip("If enabled, respawns the player when they leave the assigned terrain bounds.")]
    [SerializeField] private bool respawnIfOutsideTerrain = true;

    [Tooltip("Terrain used to detect whether the player has gone off-map. If left empty, Terrain.activeTerrain will be used when available.")]
    [SerializeField] private Terrain monitoredTerrain;

    [Tooltip("Extra world-space distance allowed beyond the terrain edge before respawning.")]
    [SerializeField] private float terrainBoundsPadding = 0f;

    [Tooltip("If enabled, respawns the player when they fall below the minimum Y value.")]
    [SerializeField] private bool respawnIfBelowY = true;

    [Tooltip("Failsafe Y level. If the player's world Y goes below this, they respawn.")]
    [SerializeField] private float minimumAllowedY = -50f;

    [Tooltip("Small cooldown to prevent repeated off-map respawns from firing every frame.")]
    [SerializeField] private float offMapRespawnCooldown = 0.5f;

    [Header("Ammo Restore")]
    [Tooltip("If true, tries to refill ammo on every weapon found in Player2Controller.guns.")]
    [SerializeField] private bool restoreAmmoOnRespawn = true;

    [Tooltip("Write debug logs when respawning/refilling.")]
    [SerializeField] private bool debugLogs = false;

    private bool isRespawning = false;
    private float lastOffMapRespawnTime = float.NegativeInfinity;
    private bool cachedPlayerControllerEnabled = true;

    private void Reset()
    {
        if (playerVitals == null)
            playerVitals = GetComponent<PlayerVitals>();

        if (playerController == null)
            playerController = GetComponent<Player2Controller>();

        if (characterController == null)
            characterController = GetComponent<CharacterController>();

        if (deathAudioSource == null)
            deathAudioSource = GetComponent<AudioSource>();

        if (monitoredTerrain == null)
            monitoredTerrain = Terrain.activeTerrain;

        InitializeCountdownUI();
    }

    private void Awake()
    {
        if (playerVitals == null)
            playerVitals = GetComponent<PlayerVitals>();

        if (playerController == null)
            playerController = GetComponent<Player2Controller>();

        if (characterController == null)
            characterController = GetComponent<CharacterController>();

        if (deathAudioSource == null)
            deathAudioSource = GetComponent<AudioSource>();

        if (monitoredTerrain == null)
            monitoredTerrain = Terrain.activeTerrain;

        InitializeCountdownUI();
    }

    private void OnEnable()
    {
        if (playerVitals != null)
            playerVitals.OnDied += HandlePlayerDied;
    }

    private void OnDisable()
    {
        if (playerVitals != null)
            playerVitals.OnDied -= HandlePlayerDied;

        SetPlayerControlEnabled(true);
        HideCountdownUI();
    }

    private void Update()
    {
        if (isRespawning)
            return;

        if (respawnPoint == null)
            return;

        if (Time.time < lastOffMapRespawnTime + offMapRespawnCooldown)
            return;

        bool belowY = respawnIfBelowY && transform.position.y < minimumAllowedY;
        bool outsideTerrain = respawnIfOutsideTerrain && IsOutsideMonitoredTerrain(transform.position);

        if (!belowY && !outsideTerrain)
            return;

        lastOffMapRespawnTime = Time.time;

        if (debugLogs)
        {
            if (belowY)
                Debug.Log("[PlayerRespawnController] Player fell below minimum Y. Respawning.", this);
            else if (outsideTerrain)
                Debug.Log("[PlayerRespawnController] Player left monitored terrain bounds. Respawning.", this);
        }

        RespawnNow();
    }

    private bool IsOutsideMonitoredTerrain(Vector3 worldPosition)
    {
        if (monitoredTerrain == null || monitoredTerrain.terrainData == null)
            return false;

        Vector3 terrainPosition = monitoredTerrain.transform.position;
        Vector3 terrainSize = monitoredTerrain.terrainData.size;

        float minX = terrainPosition.x - terrainBoundsPadding;
        float maxX = terrainPosition.x + terrainSize.x + terrainBoundsPadding;
        float minZ = terrainPosition.z - terrainBoundsPadding;
        float maxZ = terrainPosition.z + terrainSize.z + terrainBoundsPadding;

        return worldPosition.x < minX || worldPosition.x > maxX ||
               worldPosition.z < minZ || worldPosition.z > maxZ;
    }

    private void HandlePlayerDied()
    {
        if (!isRespawning)
        {
            PlayDeathSound();
            StartCoroutine(RespawnRoutine());
        }
    }

    private IEnumerator RespawnRoutine()
    {
        isRespawning = true;
        SetPlayerControlEnabled(false);

        if (respawnDelay > 0f)
            yield return StartCoroutine(ShowRespawnCountdown(respawnDelay));

        RespawnNow();
        SetPlayerControlEnabled(true);

        HideCountdownUI();
        isRespawning = false;
    }

    private IEnumerator ShowRespawnCountdown(float delay)
    {
        if (respawnCountdownText == null)
        {
            yield return new WaitForSeconds(delay);
            yield break;
        }

        if (respawnCountdownText.gameObject != null)
            respawnCountdownText.gameObject.SetActive(true);

        float timer = delay;
        int lastShownSeconds = -1;

        while (timer > 0f)
        {
            int secondsRemaining = Mathf.CeilToInt(timer);
            if (secondsRemaining != lastShownSeconds)
            {
                respawnCountdownText.text = string.Format(respawnCountdownFormat, secondsRemaining);
                lastShownSeconds = secondsRemaining;
            }

            timer -= Time.deltaTime;
            yield return null;
        }
    }

    private void PlayDeathSound()
    {
        if (deathSfx == null)
            return;

        if (deathAudioSource != null)
        {
            deathAudioSource.PlayOneShot(deathSfx, Mathf.Clamp01(deathSfxVolume));
            return;
        }

        AudioSource.PlayClipAtPoint(deathSfx, transform.position, Mathf.Clamp01(deathSfxVolume));
    }

    private void InitializeCountdownUI()
    {
        if (respawnCountdownText == null)
            return;

        respawnCountdownText.text = string.Empty;

        if (hideCountdownTextWhenIdle && respawnCountdownText.gameObject != null)
            respawnCountdownText.gameObject.SetActive(false);
    }

    private void HideCountdownUI()
    {
        if (respawnCountdownText == null)
            return;

        respawnCountdownText.text = string.Empty;

        if (hideCountdownTextWhenIdle && respawnCountdownText.gameObject != null)
            respawnCountdownText.gameObject.SetActive(false);
    }


    private void SetPlayerControlEnabled(bool enabledState)
    {
        if (playerController == null)
            return;

        if (!enabledState)
        {
            cachedPlayerControllerEnabled = playerController.enabled;
            playerController.enabled = false;
            return;
        }

        if (cachedPlayerControllerEnabled)
            playerController.enabled = true;
    }

    [ContextMenu("Respawn Now")]
    public void RespawnNow()
    {
        if (respawnPoint == null)
        {
            Debug.LogWarning("[PlayerRespawnController] No respawn point assigned.", this);
            return;
        }

        // Teleport safely with CharacterController disabled.
        bool hadCC = characterController != null;
        if (hadCC)
            characterController.enabled = false;

        Transform t = transform;
        t.position = respawnPoint.position;

        if (applyRespawnRotation)
            t.rotation = respawnPoint.rotation;

        if (hadCC)
            characterController.enabled = true;

        RestoreFullHealth();
        if (restoreAmmoOnRespawn)
            RestoreAmmo();

        if (debugLogs)
            Debug.Log("[PlayerRespawnController] Player respawned at chosen point with restored health/ammo.", this);
    }

    private void RestoreFullHealth()
    {
        if (playerVitals == null)
            return;

        playerVitals.SetHealth(playerVitals.MaxHealth, playerVitals.MaxHealth);
    }

    private void RestoreAmmo()
    {
        // First try weapons listed on Player2Controller.guns
        if (playerController != null && playerController.guns != null)
        {
            for (int i = 0; i < playerController.guns.Count; i++)
            {
                var gun = playerController.guns[i];
                if (gun == null) continue;

                Component ammo = gun.GetComponent("WeaponAmmo");
                if (ammo == null)
                    ammo = gun.GetComponentInChildren(System.Type.GetType("WeaponAmmo"), true);

                if (ammo != null)
                    TryRefillAmmoComponent(ammo);
            }
        }

        // Also try currently active ammo reference if present
        if (playerController != null && playerController.activeWeaponAmmo != null)
            TryRefillAmmoComponent(playerController.activeWeaponAmmo);
    }

    private void TryRefillAmmoComponent(object ammoObj)
    {
        if (ammoObj == null)
            return;

        System.Type type = ammoObj.GetType();

        // Common field/property name pairs used in Unity weapon scripts.
        // We try to set current values from their matching max values.
        TrySetFromMatchingMax(type, ammoObj, "currentAmmo", "maxAmmo");
        TrySetFromMatchingMax(type, ammoObj, "ammoInMag", "magazineSize");
        TrySetFromMatchingMax(type, ammoObj, "ammoInClip", "clipSize");
        TrySetFromMatchingMax(type, ammoObj, "currentMagazine", "maxMagazine");
        TrySetFromMatchingMax(type, ammoObj, "currentClip", "maxClip");
        TrySetFromMatchingMax(type, ammoObj, "reserveAmmo", "maxReserveAmmo");
        TrySetFromMatchingMax(type, ammoObj, "currentReserve", "maxReserve");
        TrySetFromMatchingMax(type, ammoObj, "storedAmmo", "maxStoredAmmo");
        TrySetFromMatchingMax(type, ammoObj, "totalAmmo", "maxTotalAmmo");

        // Some projects use a single "Refill"/"ReloadFull"/"FillAmmo" style method.
        InvokeIfExists(type, ammoObj, "Refill");
        InvokeIfExists(type, ammoObj, "RefillAmmo");
        InvokeIfExists(type, ammoObj, "RestoreAmmo");
        InvokeIfExists(type, ammoObj, "FillAmmo");
        InvokeIfExists(type, ammoObj, "ReloadFull");
        InvokeIfExists(type, ammoObj, "ResetAmmo");

        if (debugLogs)
            Debug.Log("[PlayerRespawnController] Tried to refill ammo on " + type.Name, this);
    }

    private void TrySetFromMatchingMax(System.Type type, object target, string currentName, string maxName)
    {
        object maxValue = GetMemberValue(type, target, maxName);
        if (maxValue == null)
            return;

        if (maxValue is int maxInt)
        {
            SetMemberValue(type, target, currentName, maxInt);
        }
        else if (maxValue is float maxFloat)
        {
            SetMemberValue(type, target, currentName, maxFloat);
        }
    }

    private object GetMemberValue(System.Type type, object target, string name)
    {
        FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null)
            return field.GetValue(target);

        PropertyInfo prop = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop != null && prop.CanRead)
            return prop.GetValue(target);

        return null;
    }

    private void SetMemberValue(System.Type type, object target, string name, object value)
    {
        FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null)
        {
            field.SetValue(target, value);
            return;
        }

        PropertyInfo prop = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop != null && prop.CanWrite)
        {
            prop.SetValue(target, value);
        }
    }

    private void InvokeIfExists(System.Type type, object target, string methodName)
    {
        MethodInfo method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, System.Type.EmptyTypes, null);
        if (method != null)
            method.Invoke(target, null);
    }
}
