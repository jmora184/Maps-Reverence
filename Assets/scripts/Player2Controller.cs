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
    private Vector3 gunStartPos;
    public float adsSpeed = 2f;

    [Header("Shooting")]
    public float shotsPerSecond = 10f;   // hold mouse to fire
    private float nextFireTime = 0f;

    void Start()
    {
        gunStartPos = gunHolder.localPosition;
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
            bool firing = Input.GetMouseButton(0) && Time.time >= nextFireTime;

            if (firing)
            {
                nextFireTime = Time.time + (1f / shotsPerSecond);

                if (muzzleFlash != null) muzzleFlash.SetActive(true);

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
            }
            else
            {
                if (muzzleFlash != null) muzzleFlash.SetActive(false);
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
        }

        if (Input.GetMouseButton(1))
        {
            gunHolder.position = Vector3.MoveTowards(gunHolder.position, adsPoint.position, adsSpeed * Time.deltaTime);
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
    }
}
