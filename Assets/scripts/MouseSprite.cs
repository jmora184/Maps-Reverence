using UnityEngine;

public class MouseSprite : MonoBehaviour
{
    public static MouseSprite instance;
    RectTransform rt;
    Canvas canvas;
    void Awake()
    {
        rt = GetComponent<RectTransform>();
        canvas = GetComponentInParent<Canvas>();
    }

    void Update()
    {
        rt.position = Input.mousePosition;
    }
}
