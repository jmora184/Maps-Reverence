using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TestCam : MonoBehaviour
{
    public static TestCam instance;
    public Camera cam;
    public Transform camTrans;
    private float startFOV, targetFOV;
    public GameObject[] sprites;
    public GameObject[] teamSprites;
    public float zoomSpeed = 1f;

    public Transform target;
    private void Awake()
    {
        instance = this;
    }

    void Start()
    {
        startFOV = cam.fieldOfView;
        targetFOV = startFOV;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = true;
        //GameObject sprite = GameObject.Find("Ally");
        //GameObject arrow = sprite.transform.Find("IconSprite").gameObject;
        //arrow.SetActive(false);
        sprites = GameObject.FindGameObjectsWithTag("Ally");
        foreach (GameObject x in sprites)
        {
            //Instantiate(respawnPrefab, respawn.transform.position, respawn.transform.rotation);
            GameObject arrow = x.transform.Find("IconSprite").gameObject;
            arrow.SetActive(true);
          
        }

    }

    void LateUpdate()
    {
        transform.position = target.position;
        transform.rotation = target.rotation;

        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFOV, zoomSpeed * Time.deltaTime);
    }

    public void ZoomIn(float newZoom)
    {
        targetFOV = newZoom;
    }

    public void ZoomOut(float newZoom)
    {
        targetFOV = startFOV;
    }
    void Update()
    {


        if (Input.GetKeyDown(KeyCode.Tab))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            cam.enabled = false;
            FullMini.instance.cam.enabled = true;
            FullMini.instance.cam.enabled = true;
            FullMini.instance.selectionMode =false;
            //GameObject sprite = GameObject.Find("Ally");
            //GameObject sprite2 = GameObject.Find("Ally (1)");
            //GameObject sprite3 = GameObject.Find("Ally (2)");
            //GameObject sprite4 = GameObject.Find("Ally (3)");
            //GameObject arrow = sprite.transform.Find("IconSprite").gameObject;
            //arrow.SetActive(true);
            //GameObject arrow2 = sprite2.transform.Find("IconSprite").gameObject;
            //arrow2.SetActive(true);
            //GameObject arrow3 = sprite3.transform.Find("IconSprite").gameObject;
            //arrow3.SetActive(true);
            //GameObject arrow4 = sprite4.transform.Find("IconSprite").gameObject;
            //arrow4.SetActive(true);
            Time.timeScale = .01f;
            sprites = GameObject.FindGameObjectsWithTag("Ally");
            teamSprites = GameObject.FindGameObjectsWithTag("teamSprite");
            foreach (GameObject x in sprites)
            {
                //Instantiate(respawnPrefab, respawn.transform.position, respawn.transform.rotation);
                GameObject arrow = x.transform.Find("IconSprite").gameObject;
               if(x.layer != LayerMask.NameToLayer("teamMember"))              
                {
                    arrow.SetActive(true);
                }


            }
            foreach (GameObject x in teamSprites)
            {
                //Instantiate(respawnPrefab, respawn.transform.position, respawn.transform.rotation);
                GameObject arrow4 = x.transform.Find("child").gameObject;
                if (x.layer == LayerMask.NameToLayer("teamSprite"))
                {
                    arrow4.SetActive(true);
                }


            }

        }

        if (Input.GetKeyDown(KeyCode.LeftShift))
        {
            Time.timeScale = 1f;
            //Cursor.lockState = CursorLockMode.Locked;
            //Cursor.visible = true;
            FullMini.instance.selectionMode = true;
            cam.enabled = true;
            FullMini.instance.cam.enabled = false;
            MiniUI.instance.test.SetActive(false);
            MiniUI.instance.rawImage.SetActive(true);
            FullMini.instance.canvasObj.GetComponent<Canvas>().enabled = true;
            FullMini.instance.canvasObj.SetActive(true);
            sprites = GameObject.FindGameObjectsWithTag("Ally");
            teamSprites = GameObject.FindGameObjectsWithTag("teamSprite");
            foreach (GameObject x in sprites)
            {
                //Instantiate(respawnPrefab, respawn.transform.position, respawn.transform.rotation);
                GameObject arrow = x.transform.Find("IconSprite").gameObject;
                if (arrow.activeSelf)
                {
                    arrow.SetActive(false);
                }
            }

            foreach (GameObject x in teamSprites)
            {
                //Instantiate(respawnPrefab, respawn.transform.position, respawn.transform.rotation);
                GameObject arrow7 = x.transform.Find("child").gameObject;
                if (x.layer == LayerMask.NameToLayer("teamSprite"))
                {
                    arrow7.SetActive(false);
                }


            }
            //GameObject sprite = GameObject.Find("Ally");
            //GameObject arrow = sprite.transform.Find("IconSprite").gameObject;
            //GameObject arrow2 = sprite.transform.Find("DirectionSprite").gameObject;
            //arrow.SetActive(false);
            //arrow2.SetActive(false);


        }
        if (cam.enabled == true)
        {
            if (Input.GetMouseButton(0))
            {
                Ray ray = cam.ScreenPointToRay(Input.mousePosition);
                RaycastHit hit;

                if (Physics.Raycast(ray, out hit))
                {
                    // the object identified by hit.transform was clicked
                    // do whatever you want
                }
                if (hit.collider != null)
                {
                    Debug.Log("CLICKED test cam " + hit.collider.name);
                }
            }
        }
     

        //if (Input.GetMouseButtonDown(0))
        //{
        //	RaycastHit hit;

        //	if (Physics.Raycast(camTrans.position, camTrans.forward, out hit, 50f))
        //	{

        //	}
        //          if (hit.collider != null)
        //          {
        //              Debug.Log("CLICKED " + hit.collider.name);
        //          }
        //      }


    }
}
