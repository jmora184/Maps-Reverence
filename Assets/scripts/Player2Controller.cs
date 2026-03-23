using System.Collections.Generic;
using UnityEngine;

public class Player2Controller : MonoBehaviour
{
    public static Player2Controller instance;

    [Header("Movement")]
    public float moveSpeed, gravityModifier, jumpPower, runSpeed = 12f;
    public CharacterController charCon;
    public Transform camTrans;
    private Vector3 moveInput;


    [Header("Water Slow")]
    [Tooltip("If true, movement speed is multiplied while the player is inside a WaterSlowZone trigger.")]
    public bool enableWaterSlow = true;
    [Range(0.05f, 1f)]
    public float defaultWaterSpeedMultiplier = 0.6f;

    // Runtime multiplier set by WaterSlowZone (1 = normal).
    private float _waterSpeedMultiplier = 1f;

    [Header("Look")]
    public float mouseSensitivity;
    public bool invertX;

    [Header("Weapon Spread Runtime")]
    [Tooltip("Runtime bloom (extra spread) built up while firing. Reset when switching guns.")]
    public float extraSpreadDeg = 0f;

    public bool invertY;

    

    [Header("Look Clamp (Prevents Flips)")]
    [Tooltip("Minimum camera pitch in degrees (looking down).")]
    public float minLookPitch = -60f;
    [Tooltip("Maximum camera pitch in degrees (looking up).")]
    public float maxLookPitch = 70f;

    private float _pitch; // runtime pitch accumulator (degrees)
private bool canJump, canDoubleJump;
    public Transform groundCheckPoint;
    public LayerMask whatIsGround;

    public Animator anim; // Reference to Animator component

    [Header("Footsteps (Optional)")]
    [Tooltip("Optional dedicated AudioSource for player footsteps. If left empty, one on this object will be used if available.")]
    public AudioSource footstepAudioSource;
    [Tooltip("Footstep clip played while the player is moving on the ground.")]
    public AudioClip footstepSFX;
    [Tooltip("Volume used for each footstep one-shot.")]
    public float footstepVolume = 1f;
    [Tooltip("Time between footsteps while walking.")]
    public float walkFootstepInterval = 0.5f;
    [Tooltip("Time between footsteps while running.")]
    public float runFootstepInterval = 0.33f;
    [Tooltip("Minimum horizontal movement speed before footsteps can play.")]
    public float minHorizontalSpeedForFootsteps = 0.15f;
    [Tooltip("Adds slight pitch variation so footsteps sound less repetitive.")]
    public bool randomizeFootstepPitch = true;
    public float minFootstepPitch = 0.96f;
    public float maxFootstepPitch = 1.04f;

    private float nextFootstepTime = 0f;
    private bool wasMovingForFootsteps = false;

    [Header("Gun Runtime (Active)")]
    public Gun activeGun;

    [Header("Optional: Per-Weapon Fire Kickback (no Animator)")]
    [Tooltip("If the equipped weapon (or its children) has a WeaponFireKickback component, it will play on every shot. Great for strong pistol/revolver kick without changing rifle recoil.")]
    public bool enablePerWeaponFireKickback = true;

    // Cached per-weapon kickback component from the currently equipped weapon (if any)
    private WeaponFireKickback _activeKickback;

    
    [Header("Crosshair (Per Weapon)")]
    [Tooltip("Optional crosshair manager in your UI. If left null, uses CrosshairImageManager.Instance.")]
    public CrosshairImageManager crosshairManager;


    [Header("Optional: Explicit Weapon Mapping (recommended)")]
    [Tooltip("If assigned, crosshair selection will use these exact gun references instead of name matching.")]
    public Gun pistolGun;
    [Tooltip("If assigned, crosshair selection will use these exact gun references instead of name matching.")]
    public Gun rifleGun;

    [Tooltip("If true and explicit gun refs are set, uses gun references first. If false, uses name matching.")]
    public bool preferExplicitGunRefs = true;

    [Tooltip("If true, swaps pistol/rifle crosshair assignments (quick fix if you wired sprites backwards).")]
    public bool swapPistolAndRifleCrosshairs = false;

    [Tooltip("Crosshair sprite used when pistol is equipped.")]
    public Sprite pistolCrosshairSprite;
    [Range(0.1f, 4f)] public float pistolCrosshairScale = 0.6f;

    [Tooltip("Crosshair sprite used when rifle is equipped.")]
    public Sprite rifleCrosshairSprite;
    [Range(0.1f, 4f)] public float rifleCrosshairScale = 1.0f;

    [Tooltip("If true, uses gun name to detect pistol vs rifle (contains 'pistol' or 'rifle/ar/assault').")]
    public bool detectWeaponByName = true;

[Header("Ammo UI")]
    public AmmoHUD ammoHUD;

    [Header("Runtime Ammo")]
    public WeaponAmmo activeWeaponAmmo;
    public GameObject bullet;
    public Transform firePoint;

    [Header("Bullet Ownership (Aggro)")]
    [Tooltip("Optional override for bullet owner/attacker. Set this to your Player root (tagged Player). If left empty, transform.root is used.")]
    public Transform bulletOwnerOverride;


    public GameObject muzzleFlash;
    private ParticleSystem muzzleFlashPS;
    public Transform adsPoint, gunHolder;

    [Header("Gun Switching")]
    [Tooltip("If empty, guns will be auto-found under Gun Holder (children with a Gun component).")]
    public List<Gun> guns = new List<Gun>();
    [Tooltip("Which gun index to equip on start.")]
    public int startGunIndex = 0;
    [Tooltip("Press this key to cycle weapons.")]
    public KeyCode switchGunKey = KeyCode.G;

    private int gunIndex = 0;

    [Header("Bullet Spawn Safety")]
    [Tooltip("Spawns the bullet slightly forward from the firePoint so it doesn't start inside your own colliders in ADS.")]
    public float bulletSpawnForwardOffset = 0.08f;

    private Collider[] shooterColliders;   // all colliders on player + ACTIVE weapon rig

    [Header("Recoil (Visual)")]
    [Tooltip("Assign the active weapon root (or mesh) to apply recoil without breaking ADS (Gun Holder moves for ADS).")]
    public Transform recoilTarget;
    [Tooltip("Local position kick per shot (Z negative pushes back).")]
    public Vector3 recoilPosKick = new Vector3(0f, 0f, -0.03f);
    [Tooltip("Local rotation kick per shot in degrees (X negative usually pitches up).")]
    public Vector3 recoilRotKick = new Vector3(-1.2f, 0.4f, 0f);
    [Tooltip("How quickly recoil returns to rest.")]
    public float recoilReturnSpeed = 22f;
    [Tooltip("How snappy the weapon follows the recoil target pose.")]
    public float recoilSnappiness = 38f;

    private Vector3 recoilPosCurrent, recoilPosVelocity;
    private Vector3 recoilRotCurrent, recoilRotVelocity;
    private Vector3 recoilPosBase;
    private Vector3 recoilRotBase;

    private Vector3 gunStartPos;
    private Vector3 adsLocalPos;
    public float adsSpeed = 2f;

    [Header("Shooting")]
    public float shotsPerSecond = 10f;   // hold mouse to fire (overridden by active gun FireRate if available)
    private float nextFireTime = 0f;


    [Tooltip("When true, the player cannot fire (used by reload/melee animation scripts).")]
    public bool blockShooting = false;
    [Header("Crosshair Recoil (UI)")]
    [Tooltip("How strong the crosshair recoil is while hip-firing.")]
    public float crosshairRecoilHip = 1f;
    [Tooltip("How strong the crosshair recoil is while ADS (right mouse held).")]
    public float crosshairRecoilADS = 0.45f;

    [Header("Crosshair Size")]
    [Tooltip("Base crosshair scale for most guns.")]
    public float crosshairScaleDefault = 1f;
    [Tooltip("Base crosshair scale when a pistol is active.")]
    public float crosshairScalePistol = 0.85f;


    [Header("Muzzle Flash")]
    [Tooltip("How long the muzzle flash stays visible after each shot (seconds).")]
    public float muzzleFlashDuration = 0.05f;
    private float muzzleFlashUntil = 0f;

    
    private static float NormalizeAngle(float angleDegrees)
    {
        angleDegrees %= 360f;
        if (angleDegrees > 180f) angleDegrees -= 360f;
        if (angleDegrees < -180f) angleDegrees += 360f;
        return angleDegrees;
    }

private void Awake()
    {
        instance = this;
        ResolveFootstepAudioSourceIfNeeded();
    }



    /// <summary>
    /// Called by WaterSlowZone triggers. Multiplier is clamped and applied to both walk and run speed.
    /// </summary>
    public void SetWaterSlow(bool inWater, float speedMultiplier)
    {
        if (!enableWaterSlow) return;

        if (inWater)
            _waterSpeedMultiplier = Mathf.Clamp(speedMultiplier > 0f ? speedMultiplier : defaultWaterSpeedMultiplier, 0.05f, 1f);
        else
            _waterSpeedMultiplier = 1f;
    }
    void Start()
    {
        
        // Initialize pitch from current camera local rotation (prevents sudden snap on play)
        if (camTrans != null)
            _pitch = NormalizeAngle(camTrans.localEulerAngles.x);
if (gunHolder != null)
            gunStartPos = gunHolder.localPosition;

        AutoPopulateGunsIfEmpty();
        EquipGun(Mathf.Clamp(startGunIndex, 0, Mathf.Max(0, guns.Count - 1)), true);

        // Cache crosshair base position (safe if CrosshairRecoilUI exists in scene)
        if (CrosshairRecoilUI.Instance != null)
            CrosshairRecoilUI.Instance.RebindBase();
    }

    void Update()
    {
        // Allow switching regardless of command mode (your choice). If you only want it in FPS mode,
        // wrap this in the !commandMode check below.
        if (Input.GetKeyDown(switchGunKey))
        {
            CycleGun();
        }

        // ✅ Command mode is now driven by CommandCamToggle, not FullMini
        bool commandMode =
            (CommandCamToggle.Instance != null && CommandCamToggle.Instance.IsCommandMode);

        if (!commandMode)
        {
            mouseSensitivity = 2;

            float yStore = moveInput.y;

            Vector3 vertMove = transform.forward * Input.GetAxis("Vertical");
            Vector3 horiMove = transform.right * Input.GetAxis("Horizontal");

            moveInput = horiMove + vertMove;
            moveInput.Normalize();

            float waterMult = (enableWaterSlow ? _waterSpeedMultiplier : 1f);

            if (Input.GetKey(KeyCode.LeftShift))
                moveInput *= (runSpeed * waterMult);
            else
                moveInput *= (moveSpeed * waterMult);

            moveInput.y = yStore;
            moveInput.y += Physics.gravity.y * gravityModifier * Time.deltaTime;

            if (charCon.isGrounded)
                moveInput.y = Physics.gravity.y * gravityModifier * Time.deltaTime;

            canJump = Physics.OverlapSphere(groundCheckPoint.position, .25f, whatIsGround).Length > 0;

            if (Input.GetKeyDown(KeyCode.Space) && canJump)
            {
                moveInput.y = jumpPower;
                canDoubleJump = true;
            }
            else if (canDoubleJump && Input.GetKeyDown(KeyCode.Space))
            {
                moveInput.y = jumpPower;
                canDoubleJump = false;
            }

            charCon.Move(moveInput * Time.deltaTime);
            UpdateFootsteps();

            Vector2 mouseInput = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * mouseSensitivity;

            if (invertX) mouseInput.x = -mouseInput.x;
            if (invertY) mouseInput.y = -mouseInput.y;

            // Yaw rotates the player (world Y). Pitch rotates the camera locally (prevents flips).
            transform.Rotate(0f, mouseInput.x, 0f, Space.World);

            _pitch += -mouseInput.y;
            _pitch = Mathf.Clamp(_pitch, minLookPitch, maxLookPitch);

            if (camTrans != null)
                camTrans.localRotation = Quaternion.Euler(_pitch, 0f, 0f);

            // ---------------------------
            // SHOOTING
            // ---------------------------
            if (!blockShooting)
            {
                bool holdingFire = false;

                // If the gun does NOT auto fire, shoot on mouse down only.
                if (activeGun != null && activeGun.canAutoFire == false)
                    holdingFire = Input.GetMouseButtonDown(0);
                else
                    holdingFire = Input.GetMouseButton(0);

                // Use gun fire rate if available (and non-zero), otherwise use player-level shotsPerSecond.
                float sps = shotsPerSecond;
                if (activeGun != null && activeGun.fireRate > 0)
                    sps = activeGun.fireRate;

                if (holdingFire && Time.time >= nextFireTime)
                {
                    // --- Ammo gate ---
                    // If the active weapon has ammo logic, consume a round BEFORE firing.
                    // If it returns false, we don't fire (and it may auto-start reload if reserve > 0).
                    if (activeWeaponAmmo != null && !activeWeaponAmmo.TryConsumeRound())
                        return;

                    nextFireTime = Time.time + (1f / Mathf.Max(0.01f, sps));

                    // Keep the flash visible for a short duration so it doesn't flicker inconsistently
                    muzzleFlashUntil = Time.time + muzzleFlashDuration;

                    if (firePoint != null)
                    {
                        RaycastHit hit;
                        if (Physics.Raycast(camTrans.position, camTrans.forward, out hit, 50f))
                        {
                            if (Vector3.Distance(camTrans.position, hit.point) > 2f)
                                firePoint.LookAt(hit.point);
                        }
                        else
                        {
                            firePoint.LookAt(camTrans.position + (camTrans.forward * 30f));
                        }
                    }

                    if (bullet != null && firePoint != null)
                    {
                        // ---- Weapon-specific spread (degrees) ----
                        bool isADS = Input.GetMouseButton(1);

                        float baseSpread = 0f;
                        float perShot = 0.0f;
                        float maxExtra = 0.0f;

                        if (activeGun != null)
                        {
                            baseSpread = isADS ? activeGun.adsSpreadDeg : activeGun.hipSpreadDeg;
                            perShot = activeGun.spreadPerShotDeg;
                            maxExtra = activeGun.maxExtraSpreadDeg;
                        }

                        // Build bloom while holding fire (adds on top of base spread).
                        extraSpreadDeg = Mathf.Min(extraSpreadDeg + perShot, maxExtra);

                        float finalSpread = Mathf.Max(0f, baseSpread + extraSpreadDeg);

                        // Random yaw/pitch inside a circle to form a cone.
                        Vector2 r = Random.insideUnitCircle * finalSpread;
                        Quaternion spreadRot = Quaternion.Euler(-r.y, r.x, 0f);

                        Quaternion shootRot = firePoint.rotation * spreadRot;

                        // Spawn slightly forward so ADS doesn't spawn inside our own colliders
                        Vector3 spawnPos = firePoint.position + (shootRot * Vector3.forward) * bulletSpawnForwardOffset;
                        GameObject b = Instantiate(bullet, spawnPos, shootRot);

                        // ✅ IMPORTANT: tell the bullet who fired it (needed for impact FX switching + aggro)
                        BulletController bc = b.GetComponent<BulletController>();
                        if (bc != null)
                            bc.owner = bulletOwnerOverride != null ? bulletOwnerOverride : transform.root;

                        IgnoreShooterCollisions(b);

                        // Play the equipped weapon's gunshot sound after a successful shot spawn.
                        if (activeGun != null)
                        {
                            activeGun.TriggerShotSound();
                            activeGun.TriggerFireAnimation();
                        }
                    }

                    ApplyRecoil();
                    // Optional per-weapon kickback (script-driven) for pistol/revolver
                    if (enablePerWeaponFireKickback && _activeKickback != null)
                        _activeKickback.Play();

                    // Crosshair recoil (UI) - small kick/bloom per shot
                    if (CrosshairRecoilUI.Instance != null)
                    {
                        float intensity = Input.GetMouseButton(1) ? crosshairRecoilADS : crosshairRecoilHip;
                        CrosshairRecoilUI.Instance.Kick(intensity);
                    }

                    // Ensure muzzle flash triggers every single shot (even on semi-auto).
                    TriggerMuzzleFlash();

                    // (Recovery happens below when not holding fire.)
                }

                // Recover bloom when the player isn't holding fire.
                if (activeGun != null)
                {
                    bool isHoldingFire = false;
                    if (activeGun.canAutoFire == false)
                        isHoldingFire = Input.GetMouseButtonDown(0);
                    else
                        isHoldingFire = Input.GetMouseButton(0);

                    if (!isHoldingFire)
                    {
                        extraSpreadDeg = Mathf.MoveTowards(extraSpreadDeg, 0f, activeGun.spreadRecoveryPerSec * Time.deltaTime);
                    }
                }

                // If we're using a ParticleSystem muzzle flash, we trigger it per-shot in TriggerMuzzleFlash().
                // Only use GameObject toggle mode when no ParticleSystem is present.
                if (muzzleFlash != null && muzzleFlashPS == null)
                {
                    bool showFlash = Time.time < muzzleFlashUntil;
                    if (muzzleFlash.activeSelf != showFlash)
                        muzzleFlash.SetActive(showFlash);
                }
            }
            else
            {
                // Reload / melee / etc can set blockShooting to stop the player from firing.
                muzzleFlashUntil = 0f;
                extraSpreadDeg = 0f;
                if (muzzleFlash != null) muzzleFlash.SetActive(false);
            }
        }
        else
        {
            // if command camera is on, stop muzzle flash
            if (muzzleFlash != null) muzzleFlash.SetActive(false);
            ResetFootstepTimer();
        }

        // ---------------------------
        // ADS / ZOOM
        // ---------------------------
        if (Input.GetMouseButtonDown(1))
        {
            if (TestCam.instance != null && activeGun != null)
                TestCam.instance.ZoomIn(activeGun.zoomAmount);

            RecomputeAdsLocalTarget();
        }

        if (Input.GetMouseButton(1))
        {
            if (gunHolder != null)
                gunHolder.localPosition = Vector3.MoveTowards(gunHolder.localPosition, adsLocalPos, adsSpeed * Time.deltaTime);
        }
        else
        {
            if (gunHolder != null)
                gunHolder.localPosition = Vector3.MoveTowards(gunHolder.localPosition, gunStartPos, adsSpeed * Time.deltaTime);
        }

        if (Input.GetMouseButtonUp(1))
        {
            if (TestCam.instance != null)
                TestCam.instance.ZoomOut();
        }

        // Update visual recoil (does not affect ADS gunHolder movement)
        UpdateRecoil();
    }


    private void ResolveFootstepAudioSourceIfNeeded()
    {
        if (footstepAudioSource == null)
            footstepAudioSource = GetComponent<AudioSource>();
    }

    private void ResetFootstepTimer()
    {
        nextFootstepTime = Time.time;
        wasMovingForFootsteps = false;
    }

    private void UpdateFootsteps()
    {
        if (footstepAudioSource == null || footstepSFX == null || charCon == null)
            return;

        bool groundedForFootsteps = charCon.isGrounded || canJump;
        Vector2 planarInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        bool hasMoveInput = planarInput.sqrMagnitude > 0.01f;

        if (!groundedForFootsteps || !hasMoveInput)
        {
            ResetFootstepTimer();
            return;
        }

        Vector3 horizontalVelocity = charCon.velocity;
        horizontalVelocity.y = 0f;
        float horizontalSpeed = horizontalVelocity.magnitude;
        if (horizontalSpeed < minHorizontalSpeedForFootsteps)
            return;

        bool isRunning = Input.GetKey(KeyCode.LeftShift);
        float interval = isRunning ? runFootstepInterval : walkFootstepInterval;
        interval = Mathf.Max(0.05f, interval);

        if (!wasMovingForFootsteps)
        {
            wasMovingForFootsteps = true;
            nextFootstepTime = Time.time + (interval * 0.35f);
            return;
        }

        if (Time.time < nextFootstepTime)
            return;

        float originalPitch = footstepAudioSource.pitch;
        if (randomizeFootstepPitch)
            footstepAudioSource.pitch = Random.Range(minFootstepPitch, maxFootstepPitch);
        else
            footstepAudioSource.pitch = 1f;

        footstepAudioSource.PlayOneShot(footstepSFX, footstepVolume);
        footstepAudioSource.pitch = originalPitch;

        nextFootstepTime = Time.time + interval;
    }

    private void TriggerMuzzleFlash()
    {
        if (muzzleFlash == null) return;

        // Prefer ParticleSystem for reliable per-shot flashes.
        if (muzzleFlashPS != null)
        {
            // Ensure object is enabled so it can render.
            if (!muzzleFlash.activeSelf)
                muzzleFlash.SetActive(true);

            // Restart every shot.
            muzzleFlashPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            muzzleFlashPS.Play(true);
        }
        else
        {
            // GameObject toggle flash: enable now; Update() will disable after muzzleFlashUntil.
            if (!muzzleFlash.activeSelf)
                muzzleFlash.SetActive(true);
        }
    }

    // ---------------------------
    // GUN SWITCHING
    // ---------------------------
    private void AutoPopulateGunsIfEmpty()
    {
        if (guns != null && guns.Count > 0) return;
        if (gunHolder == null) return;

        guns = new List<Gun>();
        var found = gunHolder.GetComponentsInChildren<Gun>(true);
        for (int i = 0; i < found.Length; i++)
        {
            if (!guns.Contains(found[i]))
                guns.Add(found[i]);
        }
    }

    private void CycleGun()
    {
        if (guns == null || guns.Count == 0) return;

        int next = gunIndex + 1;
        if (next >= guns.Count) next = 0;

        EquipGun(next, false);
    }

    private void EquipGun(int index, bool instant)
    {
        if (guns == null || guns.Count == 0) return;

        gunIndex = Mathf.Clamp(index, 0, guns.Count - 1);

        // Activate only the selected gun GameObject (optional, but convenient)
        for (int i = 0; i < guns.Count; i++)
        {
            if (guns[i] == null) continue;
            bool active = (i == gunIndex);
            if (guns[i].gameObject.activeSelf != active)
                guns[i].gameObject.SetActive(active);

        // Update crosshair look/size for this weapon
        ApplyCrosshairForActiveGun();

        }

        activeGun = guns[gunIndex];
        // Cache optional per-weapon kickback component (pistol/revolver can have it; rifle can omit)
        _activeKickback = null;
        if (enablePerWeaponFireKickback && activeGun != null)
            _activeKickback = activeGun.GetComponentInChildren<WeaponFireKickback>(true);
        extraSpreadDeg = 0f;

        // Pull core references from Gun component (these fields exist on your Gun script)
        if (activeGun != null)
        {
            bullet = activeGun.bullet;
            firePoint = activeGun.firePoint;

            // Ammo source lives on the same weapon GameObject as the Gun component
            // WeaponAmmo might be on the same object OR nested under the weapon visuals
            activeWeaponAmmo = activeGun.GetComponent<WeaponAmmo>();
            if (activeWeaponAmmo == null)
                activeWeaponAmmo = activeGun.GetComponentInChildren<WeaponAmmo>(true);

            // Hook the HUD so it updates when you switch weapons
            if (ammoHUD != null)
                ammoHUD.Hook(activeWeaponAmmo);
        }
        else
        {
            activeWeaponAmmo = null;
            if (ammoHUD != null)
                ammoHUD.Hook(null);
        }

        // If those aren't assigned on the Gun component, try to find common children
        if (firePoint == null && activeGun != null)
            firePoint = FindChildAny(activeGun.transform, new[] { "Fire Point", "FirePoint", "fire point", "firepoint" });

        adsPoint = null;
        if (activeGun != null)
            adsPoint = FindChildAny(activeGun.transform, new[] { "ads point", "Ads Point", "ADS Point", "adsPoint", "ADSPoint", "ads point pistol", "ads point rifle" });

        muzzleFlash = null;
        if (activeGun != null)
        {
            Transform mf = FindChildAny(activeGun.transform, new[] { "Muzzle Flash", "muzzle flash", "MuzzleFlash", "muzzleFlash" });
            muzzleFlash = (mf != null) ? mf.gameObject : null;
        }

        // Cache particle system if present (so we can Play() reliably each shot)
        muzzleFlashPS = null;
        if (muzzleFlash != null)
        {
            muzzleFlashPS = muzzleFlash.GetComponent<ParticleSystem>();
            if (muzzleFlashPS == null)
                muzzleFlashPS = muzzleFlash.GetComponentInChildren<ParticleSystem>(true);
        }

        // Recoil target: use assigned one if you want, otherwise use the active gun's transform.
        // If you want recoil on a specific mesh child, rename it and add to the list below.
        recoilTarget = null;
        if (activeGun != null)
        {
            recoilTarget = FindChildAny(activeGun.transform, new[] { "M4_8", "M1911", "Model", "Mesh", "Rifle", "Pistol" });
            if (recoilTarget == null)
                recoilTarget = activeGun.transform;
        }

        // Reset recoil baselines
        recoilPosCurrent = Vector3.zero;
        recoilRotCurrent = Vector3.zero;
        recoilPosVelocity = Vector3.zero;
        recoilRotVelocity = Vector3.zero;

        if (recoilTarget != null)
        {
            recoilPosBase = recoilTarget.localPosition;
            recoilRotBase = recoilTarget.localEulerAngles;
        }

        // Recompute shooter colliders to ignore self hits (player + ACTIVE gun only)
        GatherShooterColliders();

        // Recompute ADS target & optionally snap gunHolder to hip position when switching
        RecomputeAdsLocalTarget();

        if (instant && gunHolder != null)
            gunHolder.localPosition = gunStartPos;

        // Clear muzzle flash state so it doesn't "stick" across switches
        muzzleFlashUntil = 0f;
                extraSpreadDeg = 0f;
        if (muzzleFlash != null) muzzleFlash.SetActive(false);
        if (muzzleFlashPS != null) muzzleFlashPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        // Update crosshair base scale based on the equipped gun
        UpdateCrosshairScale();

        // Reset fire timer so switching doesn't "eat" the next shot
        nextFireTime = 0f;
    }

    private void RecomputeAdsLocalTarget()
    {
        if (adsPoint != null && gunHolder != null && gunHolder.parent != null)
            adsLocalPos = gunHolder.parent.InverseTransformPoint(adsPoint.position);
        else
            adsLocalPos = gunStartPos;
    }

    private void GatherShooterColliders()
    {
        var playerCols = GetComponentsInChildren<Collider>(true);

        Collider[] gunCols = null;
        if (activeGun != null)
            gunCols = activeGun.GetComponentsInChildren<Collider>(true);

        shooterColliders = Combine(playerCols, gunCols);
    }


    private void IgnoreShooterCollisions(GameObject bulletObj)
    {
        if (bulletObj == null) return;

        // Prefer collider on root; fallback to any child collider.
        Collider bulletCol = bulletObj.GetComponent<Collider>();
        if (bulletCol == null)
            bulletCol = bulletObj.GetComponentInChildren<Collider>(true);

        if (bulletCol == null) return;

        if (shooterColliders == null) return;

        for (int i = 0; i < shooterColliders.Length; i++)
        {
            var c = shooterColliders[i];
            if (c == null || c == bulletCol) continue;
            Physics.IgnoreCollision(bulletCol, c, true);
        }
    }

    private static Transform FindChildAny(Transform root, string[] names)
    {
        if (root == null || names == null || names.Length == 0) return null;

        // Simple DFS over children
        var stack = new Stack<Transform>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var t = stack.Pop();
            string tn = t.name;

            for (int i = 0; i < names.Length; i++)
            {
                string n = names[i];
                if (string.IsNullOrEmpty(n)) continue;

                // Exact or partial match (case-insensitive). Partial match helps for names like "ads point Pistol".
                if (string.Equals(tn, n, System.StringComparison.OrdinalIgnoreCase) ||
                    tn.IndexOf(n, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return t;
                }
            }

            for (int c = 0; c < t.childCount; c++)
                stack.Push(t.GetChild(c));
        }

        return null;
    }
    private static Collider[] Combine(Collider[] a, Collider[] b)
    {
        if (a == null || a.Length == 0) return b ?? new Collider[0];
        if (b == null || b.Length == 0) return a;

        var list = new List<Collider>(a.Length + b.Length);
        list.AddRange(a);
        for (int i = 0; i < b.Length; i++)
        {
            if (b[i] == null) continue;
            if (!list.Contains(b[i]))
                list.Add(b[i]);
        }
        return list.ToArray();
    }

    // ---------------------------
    // RECOIL (VISUAL)
    // ---------------------------
    private void ApplyRecoil()
    {
        if (recoilTarget == null) return;

        recoilPosCurrent += recoilPosKick;
        recoilRotCurrent += recoilRotKick;
    }

    private void UpdateRecoil()
    {
        if (recoilTarget == null) return;

        recoilPosCurrent = Vector3.SmoothDamp(recoilPosCurrent, Vector3.zero, ref recoilPosVelocity,
            1f / Mathf.Max(0.01f, recoilReturnSpeed));
        recoilRotCurrent = Vector3.SmoothDamp(recoilRotCurrent, Vector3.zero, ref recoilRotVelocity,
            1f / Mathf.Max(0.01f, recoilReturnSpeed));

        Vector3 targetPos = recoilPosBase + recoilPosCurrent;
        Vector3 targetRot = recoilRotBase + recoilRotCurrent;

        recoilTarget.localPosition = Vector3.Lerp(recoilTarget.localPosition, targetPos, Time.deltaTime * recoilSnappiness);
        recoilTarget.localRotation = Quaternion.Slerp(recoilTarget.localRotation, Quaternion.Euler(targetRot), Time.deltaTime * recoilSnappiness);
    }

    private void UpdateCrosshairScale()
    {
        if (CrosshairRecoilUI.Instance == null) return;

        // Decide if this gun is a "pistol" by gunName or transform name.
        bool isPistol = false;
        if (activeGun != null)
        {
            string n = activeGun.gunName;
            if (string.IsNullOrEmpty(n)) n = activeGun.transform.name;

            n = n.ToLowerInvariant();
            isPistol = n.Contains("pistol") || n.Contains("m1911");
        }

        float baseScale = isPistol ? crosshairScalePistol : crosshairScaleDefault;
        CrosshairRecoilUI.Instance.SetBaseScale(baseScale);
    }

    private void ApplyCrosshairForActiveGun()
    {
        var mgr = (crosshairManager != null) ? crosshairManager : CrosshairImageManager.Instance;
        if (mgr == null) return;

        if (activeGun == null)
        {
            mgr.ResetToDefault();
            return;
        }

        // Decide which crosshair to use
        bool isPistol = false;
        bool isRifle = false;

        // 1) Prefer explicit gun refs (most reliable)
        if (preferExplicitGunRefs && (pistolGun != null || rifleGun != null))
        {
            isPistol = (pistolGun != null && activeGun == pistolGun);
            isRifle  = (rifleGun  != null && activeGun == rifleGun);
        }

        // 2) Fallback to name matching
        if (!isPistol && !isRifle && detectWeaponByName)
        {
            string n = activeGun.gameObject.name.ToLowerInvariant();
            isPistol = n.Contains("pistol");
            isRifle = n.Contains("rifle") || n.Contains("ar") || n.Contains("assault");
        }

        // Optional: swap assignments if you wired sprites backwards
        Sprite pistolSprite = swapPistolAndRifleCrosshairs ? rifleCrosshairSprite : pistolCrosshairSprite;
        float  pistolScale  = swapPistolAndRifleCrosshairs ? rifleCrosshairScale  : pistolCrosshairScale;

        Sprite rifleSprite = swapPistolAndRifleCrosshairs ? pistolCrosshairSprite : rifleCrosshairSprite;
        float  rifleScale  = swapPistolAndRifleCrosshairs ? pistolCrosshairScale  : rifleCrosshairScale;

        if (isPistol && pistolSprite != null)
        {
            mgr.SetCrosshair(pistolSprite, pistolScale);
        }
        else if (isRifle && rifleSprite != null)
        {
            mgr.SetCrosshair(rifleSprite, rifleScale);
        }
        else
        {
            // Fallback: if no match or sprites not assigned, just reset
            mgr.ResetToDefault();
        }
    }


}