using UnityEngine;

public class DirectionSprite : MonoBehaviour
{
    private SpriteRenderer spriteRenderer;
    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            //spriteRenderer.enabled = false; // Ensure the SpriteRenderer is disabled at runtime
        }
    }

    void Update()
    {
        //spriteRenderer.transform.rotation = Quaternion.Euler(90.67902f, 3.2f, 0f);

        if (Input.GetKeyDown(KeyCode.Tab))
        {
            spriteRenderer.enabled = true;
        }

        if (Input.GetKeyDown(KeyCode.LeftShift))
        {
            spriteRenderer.enabled = false;
        }

    }
}
