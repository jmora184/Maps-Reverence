using UnityEngine;

public class HealthSprite : MonoBehaviour
{
    private SpriteRenderer spriteRenderer;
    public Vector3 initialPosition;
    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        initialPosition = spriteRenderer.transform.position;
        if (spriteRenderer != null)
        {
            spriteRenderer.enabled = false; // Ensure the SpriteRenderer is disabled at runtime
        }
    }

    void Update()
    {


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
