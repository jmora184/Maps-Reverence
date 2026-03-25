using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Flashes left/right/forward/behind UI arrows based on where the hit came from.
/// Attach this to any object in the scene (often your Canvas "Indicator" parent)
/// and assign the arrow Images in the inspector.
/// </summary>
public class DamageDirectionIndicator : MonoBehaviour
{
    [Header("Arrow Images")]
    [SerializeField] private Image leftArrow;
    [SerializeField] private Image rightArrow;
    [SerializeField] private Image forwardArrow;
    [SerializeField] private Image behindArrow;

    [Header("Direction Reference")]
    [Tooltip("Usually the Player root transform. If left empty, PlayerVitals.Instance is used when available.")]
    [SerializeField] private Transform directionReference;

    [Header("Flash")]
    [Range(0f, 1f)]
    [SerializeField] private float maxAlpha = 0.9f;

    [Tooltip("How fast the flash fades back out.")]
    [SerializeField] private float fadeSpeed = 4.5f;

    [Tooltip("Set the arrows fully transparent on start.")]
    [SerializeField] private bool hideOnStart = true;

    private float leftStrength;
    private float rightStrength;
    private float forwardStrength;
    private float behindStrength;

    private Color leftBaseColor = Color.white;
    private Color rightBaseColor = Color.white;
    private Color forwardBaseColor = Color.white;
    private Color behindBaseColor = Color.white;

    private void Awake()
    {
        if (directionReference == null && PlayerVitals.Instance != null)
            directionReference = PlayerVitals.Instance.transform;

        CacheBaseColors();

        if (hideOnStart)
            ApplyImmediateAlpha(0f, 0f, 0f, 0f);
    }

    private void Update()
    {
        leftStrength = Mathf.MoveTowards(leftStrength, 0f, fadeSpeed * Time.deltaTime);
        rightStrength = Mathf.MoveTowards(rightStrength, 0f, fadeSpeed * Time.deltaTime);
        forwardStrength = Mathf.MoveTowards(forwardStrength, 0f, fadeSpeed * Time.deltaTime);
        behindStrength = Mathf.MoveTowards(behindStrength, 0f, fadeSpeed * Time.deltaTime);

        ApplyCurrentAlpha();
    }

    public void SetDirectionReference(Transform newReference)
    {
        directionReference = newReference;
    }

    public void FlashFromWorldPosition(Vector3 sourceWorldPosition)
    {
        if (directionReference == null)
        {
            if (PlayerVitals.Instance != null)
                directionReference = PlayerVitals.Instance.transform;
            else
                return;
        }

        Vector3 toSource = sourceWorldPosition - directionReference.position;
        toSource.y = 0f;

        if (toSource.sqrMagnitude <= 0.0001f)
            return;

        Vector3 local = directionReference.InverseTransformDirection(toSource.normalized);

        float absX = Mathf.Abs(local.x);
        float absZ = Mathf.Abs(local.z);

        // Front/behind take priority when the attacker is mainly in front or behind the player.
        if (absZ >= absX)
        {
            if (local.z > 0f)
            {
                forwardStrength = 1f;
                return;
            }

            if (local.z < 0f)
            {
                behindStrength = 1f;
                return;
            }
        }

        // Otherwise prefer the side.
        if (local.x < 0f)
            leftStrength = 1f;
        else if (local.x > 0f)
            rightStrength = 1f;
    }

    private void CacheBaseColors()
    {
        if (leftArrow != null) leftBaseColor = leftArrow.color;
        if (rightArrow != null) rightBaseColor = rightArrow.color;
        if (forwardArrow != null) forwardBaseColor = forwardArrow.color;
        if (behindArrow != null) behindBaseColor = behindArrow.color;
    }

    private void ApplyCurrentAlpha()
    {
        if (leftArrow != null)
        {
            Color c = leftBaseColor;
            c.a = leftStrength * maxAlpha;
            leftArrow.color = c;
        }

        if (rightArrow != null)
        {
            Color c = rightBaseColor;
            c.a = rightStrength * maxAlpha;
            rightArrow.color = c;
        }

        if (forwardArrow != null)
        {
            Color c = forwardBaseColor;
            c.a = forwardStrength * maxAlpha;
            forwardArrow.color = c;
        }

        if (behindArrow != null)
        {
            Color c = behindBaseColor;
            c.a = behindStrength * maxAlpha;
            behindArrow.color = c;
        }
    }

    private void ApplyImmediateAlpha(float left, float right, float forward, float behind)
    {
        leftStrength = Mathf.Clamp01(left);
        rightStrength = Mathf.Clamp01(right);
        forwardStrength = Mathf.Clamp01(forward);
        behindStrength = Mathf.Clamp01(behind);
        ApplyCurrentAlpha();
    }

#if UNITY_EDITOR
    [ContextMenu("TEST Left")]
    private void TestLeft() => leftStrength = 1f;

    [ContextMenu("TEST Right")]
    private void TestRight() => rightStrength = 1f;

    [ContextMenu("TEST Forward")]
    private void TestForward() => forwardStrength = 1f;

    [ContextMenu("TEST Behind")]
    private void TestBehind() => behindStrength = 1f;
#endif
}
