// 1/5/2026 AI-Tag
// This was created with the help of Assistant, a Unity Artificial Intelligence product.

using UnityEngine;

public class EnemyIconSpriteController : MonoBehaviour
{
    private SpriteRenderer spriteRenderer;

    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            spriteRenderer.enabled = false; // Ensure the SpriteRenderer is disabled at runtime
        }
    }

    void Update()
    {
        spriteRenderer.transform.rotation = Quaternion.Euler(-450.339f, 1221.701f, -3.352f);
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