using UnityEngine;
using UnityEngine.UI;

public class PlayerIconGradientPulse : MonoBehaviour
{
    public Image glowOverlay;

    [Header("Pulse")]
    public float pulseSpeed = 2f;
    public float minAlpha = 0f;
    public float maxAlpha = 0.55f;

    [Header("Optional scale pulse")]
    public bool scalePulse = true;
    public float scaleAmount = 0.06f;

    Vector3 _startScale;

    void Awake()
    {
        _startScale = transform.localScale;
    }

    void Update()
    {
        float t = (Mathf.Sin(Time.unscaledTime * pulseSpeed) + 1f) * 0.5f; // 0..1

        if (scalePulse)
        {
            float s = 1f + (t - 0.5f) * 2f * scaleAmount;
            transform.localScale = _startScale * s;
        }

        if (glowOverlay != null)
        {
            var c = glowOverlay.color;
            c.a = Mathf.Lerp(minAlpha, maxAlpha, t);
            glowOverlay.color = c;
        }
    }
}
