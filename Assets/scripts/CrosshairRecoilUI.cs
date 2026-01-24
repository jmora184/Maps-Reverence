using UnityEngine;

public class CrosshairRecoilUI : MonoBehaviour
{
    public static CrosshairRecoilUI Instance { get; private set; }

    [Header("Kick")]
    public Vector2 kickPixels = new Vector2(0f, 6f);     // up a little
    public float kickRandomX = 2.5f;                    // small sideways jitter
    public float kickRandomY = 1.5f;

    [Header("Return")]
    public float returnSpeed = 18f;                      // higher = faster return
    public float snappiness = 35f;

    [Header("Bloom (optional)")]
    public bool useScaleBloom = true;
    public float bloomPerShot = 0.06f;                   // scale add per shot
    public float bloomMax = 0.35f;                       // cap
    public float bloomReturnSpeed = 10f;

    private RectTransform rt;
    private Vector2 basePos;
    private Vector2 offset;
    private Vector2 offsetVel;

    private float bloom;
    private float bloomVel;

    // Base crosshair scale (weapon-specific). Final scale = baseScale * (1 + bloom).
    private float baseScale = 1f;

    private void Awake()
    {
        Instance = this;
        rt = transform as RectTransform;
        basePos = rt.anchoredPosition;
        baseScale = rt.localScale.x;
    }

    public void Kick(float intensity = 1f)
    {
        // kick a bit (pixels) + some randomness
        float rx = Random.Range(-kickRandomX, kickRandomX) * intensity;
        float ry = Random.Range(-kickRandomY, kickRandomY) * intensity;

        offset += (kickPixels * intensity) + new Vector2(rx, ry);

        if (useScaleBloom)
        {
            bloom = Mathf.Min(bloom + bloomPerShot * intensity, bloomMax);
        }
    }

    private void Update()
    {
        // return position offset toward 0
        offset = Vector2.SmoothDamp(offset, Vector2.zero, ref offsetVel, 1f / Mathf.Max(0.01f, returnSpeed));
        rt.anchoredPosition = Vector2.Lerp(rt.anchoredPosition, basePos + offset, Time.deltaTime * snappiness);

        // return bloom toward 0
        if (useScaleBloom)
        {
            bloom = Mathf.SmoothDamp(bloom, 0f, ref bloomVel, 1f / Mathf.Max(0.01f, bloomReturnSpeed));
            float s = baseScale * (1f + bloom);
            rt.localScale = new Vector3(s, s, 1f);
        }
    }

    // Optional: call this if you ever move the crosshair base position at runtime
    public void RebindBase()
    {
        if (rt == null) rt = transform as RectTransform;
        basePos = rt.anchoredPosition;
        baseScale = rt.localScale.x;
    }

    /// <summary>
    /// Sets the base crosshair scale (e.g., smaller for pistols). Bloom is applied on top of this.
    /// </summary>
    public void SetBaseScale(float scale)
    {
        if (rt == null) rt = transform as RectTransform;

        baseScale = Mathf.Max(0.01f, scale);

        // Apply immediately using current bloom so switching feels instant.
        float s = useScaleBloom ? baseScale * (1f + bloom) : baseScale;
        rt.localScale = new Vector3(s, s, 1f);
    }

}
