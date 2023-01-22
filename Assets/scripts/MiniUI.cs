using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MiniUI : MonoBehaviour
{
    // Start is called before the first frame update
    public static MiniUI instance;
    public GameObject button;
    public GameObject moveButton;
    public GameObject splitButton;
    public GameObject cancelButton;
    public GameObject test;
    public GameObject rawImage;
    public bool zoomMode = false;
    public RectTransform rt;
    private void Awake()
    {
        instance = this;
    }
    void Start()
    {
        rt = rawImage.GetComponent<RectTransform>();
    }

    // Update is called once per frame
    void Update()
    {

        if (Input.GetKeyDown(KeyCode.M))
        {
            if(rt.sizeDelta == new Vector2(270, 270))
            {
     
                rt.sizeDelta = new Vector2(1000, 1000);
                rt.localPosition = new Vector3(0, 0, 0);
            }
            else
            {
                rt.sizeDelta = new Vector2(270, 270);
                rt.localPosition = new Vector3(800, -372, 0);
            }

        }

    }

    public void ff()
    {
        Debug.Log("addclick");
        FullMini.instance.add();
    }

    public void mm()
    {
        FullMini.instance.move();
    }
    public void ss()
    {
        FullMini.instance.split();
    }

    public void cx()
    {
        FullMini.instance.cancel();
    }

    public void testc()
    {
        Debug.Log("addclick");
    }
}
