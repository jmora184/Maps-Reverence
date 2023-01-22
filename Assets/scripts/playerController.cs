using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class playerController : MonoBehaviour
{
    public static playerController instance;
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
    public Animator anim;

    public GameObject bullet;
    public Transform firePoint;

    public Gun activeGun;
    public List<Gun> allGuns = new List<Gun>();
    public List<Gun> unlockableGuns = new List<Gun>();
    public int currentGun;

    public Transform adsPoint, gunHolder;
    private Vector3 gunStartPos;
    public float adsSpeed = 2f;

    public GameObject muzzleFlash;
    public Camera cam;
    private float bounceAmount;
    private bool bounce;
    public float maxViewAngle = 60;
    // Start is called before the first frame update
    void Start()
    {
        CameraController.instance.theCam.enabled=false;
        MiniMap.instance.cam.enabled = true;
        currentGun--;
        SwitchGun();

        gunStartPos = gunHolder.localPosition;
    }

    private void Awake()
    {
        instance = this;
    }

    // Update is called once per frame
    void Update()
    {

        if (Input.GetMouseButton(0))
        {
            //Ray ray = MiniMap.instance.cam.ScreenPointToRay(Input.mousePosition);
            //RaycastHit hit;

            //if (Physics.Raycast(ray, out hit))
            //{
            //    // the object identified by hit.transform was clicked
            //    // do whatever you want
            //}
            //if (hit.collider != null)
            //{
            //    Debug.Log("CLICKED " + hit.collider.name);
            //}
        }
        if (!UIController.instance.pauseScreen.activeInHierarchy && !GameManager.instance.levelEnding)
        {

            if (Input.GetMouseButton(0))
            {
                Ray ray = MiniMap.instance.cam.ScreenPointToRay(Input.mousePosition);
                RaycastHit hit;

                if (Physics.Raycast(ray, out hit))
                {
                    // the object identified by hit.transform was clicked
                    // do whatever you want
                }
                if (hit.collider != null)
                {
                    Debug.Log("CLICKED " + hit.collider.name);
                }
            }
            mouseSensitivity = 2;
            //moveInput.x = Input.GetAxis("Horizontal") * moveSpeed * Time.deltaTime;
            //moveInput.z = Input.GetAxis("Vertical") * moveSpeed * Time.deltaTime;

            //store y velocity
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
            moveInput = moveInput * moveSpeed;

            moveInput.y = yStore;

            moveInput.y += Physics.gravity.y * gravityModifier * Time.deltaTime;



            if (charCon.isGrounded)
            {
                moveInput.y = Physics.gravity.y * gravityModifier * Time.deltaTime;
            }

            canJump = Physics.OverlapSphere(groundCheckPoint.position, .2f, whatIsGround).Length > 0;
            //handle jumping

            if (canJump)
            {
                canDoubleJump = false;
            }
            if (Input.GetKeyDown(KeyCode.Space) && canJump)
            {
                moveInput.y = jumpPower;

                canDoubleJump = true;
            }
            else if (Input.GetKeyDown(KeyCode.Space) && canDoubleJump)
            {
                moveInput.y = jumpPower;

                canDoubleJump = false;
            }
            if (bounce)
            {
                bounce = false;
                moveInput.y = bounceAmount;

                canDoubleJump = true;

            }
            charCon.Move(moveInput * Time.deltaTime);

            //control camera rotation
            Vector2 mouseInput = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * mouseSensitivity;

            if (invertX)
            {
                mouseInput.x = -mouseInput.x;
            }

            if (invertY)
            {
                mouseInput.y = -mouseInput.y;
            }

            transform.rotation = Quaternion.Euler(transform.rotation.eulerAngles.x, transform.rotation.eulerAngles.y + mouseInput.x, transform.rotation.eulerAngles.z);

            camTrans.rotation = Quaternion.Euler(camTrans.rotation.eulerAngles + new Vector3(-mouseInput.y, 0f, 0f));

            if(camTrans.rotation.eulerAngles.x > maxViewAngle && camTrans.rotation.eulerAngles.x < 180f)
            {
                camTrans.rotation = Quaternion.Euler(maxViewAngle, camTrans.rotation.eulerAngles.y, camTrans.rotation.eulerAngles.z);
            }
            else if (camTrans.rotation.eulerAngles.x >180f && camTrans.rotation.eulerAngles.x <360 - maxViewAngle)
            {
                camTrans.rotation = Quaternion.Euler(-maxViewAngle, camTrans.rotation.eulerAngles.y, camTrans.rotation.eulerAngles.z);
            }
            muzzleFlash.SetActive(false);

            //Handle Shooting
            //single shots
            if (Input.GetMouseButtonDown(0) && activeGun.fireCounter <= 0)
            {
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
                //Instantiate(bullet, firePoint.position, firePoint.rotation);
                FireShot();
            }

            //repeat shots
            if (Input.GetMouseButton(0) && activeGun.canAutoFire)
            {
                if (activeGun.fireCounter <= 0)
                {
                    FireShot();
                }
            }

            if (Input.GetKeyDown(KeyCode.Tab))
            {
                SwitchGun();
            }

            //if (Input.GetMouseButtonDown(1))
            //{
            //    CameraController.instance.ZoomIn(activeGun.zoomAmount);
            //}
            if (Input.GetMouseButton(1))
            {
                gunHolder.position = Vector3.MoveTowards(gunHolder.position, adsPoint.position, adsSpeed * Time.deltaTime);

            }
            else
            {
                gunHolder.localPosition = Vector3.MoveTowards(gunHolder.localPosition, gunStartPos, adsSpeed * Time.deltaTime);
            }
            //if (Input.GetMouseButtonUp(1))
            //{
            //    CameraController.instance.ZoomOut(activeGun.zoomAmount);
            //}
            //anim.SetFloat("moveSpeed", moveInput.magnitude);
            //anim.SetBool("onGround", canJump);
        }
    }
    void OnMouseDown()
    {
        // this object was clicked - do something
        // this object was clicked - do something
        Debug.Log("clicked");
    }
    public void FireShot()
    {
        if(activeGun.currentAmmo > 0)
        {
        activeGun.currentAmmo--;
        Instantiate(activeGun.bullet, firePoint.position, firePoint.rotation);

        activeGun.fireCounter = activeGun.fireRate;

            UIController.instance.ammoText.text = "AMMO:" + activeGun.currentAmmo;

            muzzleFlash.SetActive(true);
        }

    }

    public void SwitchGun()
    {
        activeGun.gameObject.SetActive(false);
        currentGun++;

        if (currentGun >= allGuns.Count)
        {
            currentGun = 0;
        }
        activeGun = allGuns[currentGun];
        activeGun.gameObject.SetActive(true);
        UIController.instance.ammoText.text = "AMMO:" + activeGun.currentAmmo;

        firePoint.position = activeGun.firePoint.position;
    }

    public void AddGun(string gunToAdd)
    {
        bool gunUnlocked = false;

        if(unlockableGuns.Count > 0)
        {
            for(int i =0; i < unlockableGuns.Count; i++)
            {
                if(unlockableGuns[i].gunName == gunToAdd)
                {
                    gunUnlocked = true;
                    allGuns.Add(unlockableGuns[i]);

                    unlockableGuns.RemoveAt(i);

                    i = unlockableGuns.Count;
                }
            }
        }

        if (gunUnlocked)
        {
            currentGun = allGuns.Count - 2;
            SwitchGun();
        }
    }

    public void Bounce(float bounceForce)
    {
        bounceAmount = bounceForce;
        bounce = true;
    }
}