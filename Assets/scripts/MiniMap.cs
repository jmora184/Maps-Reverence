using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MiniMap : MonoBehaviour
{
	public static MiniMap instance;
	public Camera cam;
	public Transform camTrans;
	void Start()
	{
        cam.enabled = true;
        //TestCam.instance.cam.enabled = false;
	}

	private void Awake()
	{
		instance = this;
	}

	void LateUpdate()
    {

    }
	void Update()
	{
        //TestCam.instance.cam.enabled = true;
        //if (Input.GetMouseButton(0))
        //{
        //    Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        //    RaycastHit hit;

        //    if (Physics.Raycast(ray, out hit))
        //    {
        //        // the object identified by hit.transform was clicked
        //        // do whatever you want
        //    }
        //    if (hit.collider != null)
        //    {
        //        Debug.Log("CLICKED in minimap" + hit.collider.name);
        //    }
        //}


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
