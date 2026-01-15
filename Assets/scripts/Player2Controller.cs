using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Player2Controller : MonoBehaviour
{
    public static Player2Controller instance;

    public float moveSpeed, gravityModifier, jumpPower, runSpeed = 12f;
    public CharacterController charCon;
    public Transform camTrans;
    private Vector3 moveInput;

    public float mouseSensitivity;
    public bool invertX;
    public bool invertY;

    private bool canJump, canDoubleJump;
    public Transform groundCheckPoint;
    public LayerMask whatIsGround;

    public Animator anim; // Reference to Animator component

    public Gun activeGun;
    public GameObject bullet;
    public Transform firePoint;

    public GameObject muzzleFlash;

    public Transform adsPoint, gunHolder;

    [Header("Recoil (Visual)")]
    [Tooltip("Assign Rifle (recommended) to apply recoil without breaking ADS (Gun Holder moves for ADS).")]
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
    public float shotsPerSecond = 10f;   // hold mouse to fire
    private float nextFireTime = 0f;

    [Header("Crosshair Recoil (UI)")]
    [Tooltip("How strong the crosshair recoil is while hip-firing.")]
    public float crosshairRecoilHip = 1f;
    [Tooltip("How strong the crosshair recoil is while ADS (right mouse held).")]
    public float crosshairRecoilADS = 0.45f;

    [Header("Muzzle Flash")]
    [Tooltip("How long the muzzle flash stays visible after each shot (seconds).")]
    public float muzzleFlashDuration = 0.05f;
    private float muzzleFlashUntil = 0f;

    void Start()
    {
        if (gunHolder != null)
            gunStartPos = gunHolder.localPosition;

        // Cache ADS target as a LOCAL position (more reliable than world-space moves when parented)
        if (adsPoint != null && gunHolder != null && gunHolder.parent != null)
            adsLocalPos = gunHolder.parent.InverseTransformPoint(adsPoint.position);
        else
            adsLocalPos = gunStartPos;

        // --- Recoil setup ---
        // IMPORTANT: recoil should NOT be applied to gunHolder, because gunHolder is what we move for ADS.
        // Default to the Rifle child if it exists.
        if (recoilTarget == null)
        {
            if (gunHolder != null)
            {
                var rifle = gunHolder.Find("Rifle");
                if (rifle != null) recoilTarget = rifle;
            }

            if (recoilTarget == null && firePoint != null && firePoint.parent != null)
                recoilTarget = firePoint.parent;
        }

        if (recoilTarget != null)
        {
            recoilPosBase = recoilTarget.localPosition;
            recoilRotBase = recoilTarget.localEulerAngles;
        }

        // Cache crosshair base position (safe if CrosshairRecoilUI exists in scene)
        if (CrosshairRecoilUI.Instance != null)
            CrosshairRecoilUI.Instance.RebindBase();
    }

    private void Awake()
    {
        instance = this;
    }

    void Update()
    {
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

            if (Input.GetKey(KeyCode.LeftShift))
            {
                moveInput = moveInput * runSpeed;
            }
            else
            {
                moveInput = moveInput * moveSpeed;
            }

            moveInput.y = yStore;
            moveInput.y += Physics.gravity.y * gravityModifier * Time.deltaTime;

            if (charCon.isGrounded)
            {
                moveInput.y = Physics.gravity.y * gravityModifier * Time.deltaTime;
            }

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

            Vector2 mouseInput = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * mouseSensitivity;

            if (invertX) mouseInput.x = -mouseInput.x;
            if (invertY) mouseInput.y = -mouseInput.y;

            transform.rotation = Quaternion.Euler(
                transform.rotation.eulerAngles.x,
                transform.rotation.eulerAngles.y + mouseInput.x,
                transform.rotation.eulerAngles.z
            );

            camTrans.rotation = Quaternion.Euler(camTrans.rotation.eulerAngles + new Vector3(-mouseInput.y, 0f, 0f));

            // ---------------------------
            // SHOOTING (MACHINE GUN)
            // ---------------------------
            bool holdingFire = Input.GetMouseButton(0);

            if (holdingFire && Time.time >= nextFireTime)
            {
                nextFireTime = Time.time + (1f / shotsPerSecond);

                // Keep the flash visible for a short duration so it doesn't flicker inconsistently
                muzzleFlashUntil = Time.time + muzzleFlashDuration;

                RaycastHit hit;
                if (Physics.Raycast(camTrans.position, camTrans.forward, out hit, 50f))
                {
                    if (Vector3.Distance(camTrans.position, hit.point) > 2f)
                    {
                        firePoint.LookAt(hit.point);
                    }
                }
                else
                {
                    firePoint.LookAt(camTrans.position + (camTrans.forward * 30f));
                }

                Instantiate(bullet, firePoint.position, firePoint.rotation);

                ApplyRecoil();

                // Crosshair recoil (UI) - small kick/bloom per shot
                if (CrosshairRecoilUI.Instance != null)
                {
                    float intensity = Input.GetMouseButton(1) ? crosshairRecoilADS : crosshairRecoilHip;
                    CrosshairRecoilUI.Instance.Kick(intensity);
                }
            }

            // Drive muzzle flash visibility from the timer (stable across frame rates)
            if (muzzleFlash != null)
            {
                bool showFlash = holdingFire && Time.time < muzzleFlashUntil;
                if (muzzleFlash.activeSelf != showFlash)
                    muzzleFlash.SetActive(showFlash);
            }
        }
        else
        {
            // if command camera is on, stop muzzle flash
            if (muzzleFlash != null) muzzleFlash.SetActive(false);
        }

        // ---------------------------
        // ADS / ZOOM
        // ---------------------------
        if (Input.GetMouseButtonDown(1))
        {
            if (TestCam.instance != null && activeGun != null)
                TestCam.instance.ZoomIn(activeGun.zoomAmount);

            // Recompute ADS local target (in case ADS point is moved/animated)
            if (adsPoint != null && gunHolder != null && gunHolder.parent != null)
                adsLocalPos = gunHolder.parent.InverseTransformPoint(adsPoint.position);
        }

        if (Input.GetMouseButton(1))
        {
            gunHolder.localPosition = Vector3.MoveTowards(gunHolder.localPosition, adsLocalPos, adsSpeed * Time.deltaTime);
        }
        else
        {
            gunHolder.localPosition = Vector3.MoveTowards(gunHolder.localPosition, gunStartPos, adsSpeed * Time.deltaTime);
        }

        if (Input.GetMouseButtonUp(1))
        {
            if (TestCam.instance != null)
                TestCam.instance.ZoomOut(); // ✅ FIXED: no argument
        }


        // Update visual recoil (does not affect ADS gunHolder movement)
        UpdateRecoil();
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

}
