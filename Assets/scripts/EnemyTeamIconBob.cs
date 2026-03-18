using UnityEngine;

/// <summary>
/// Simple bobbing motion for the EnemyTeamIconPrefab.
/// Keeps the change isolated from targeting/arrow scripts.
/// 
/// Attach this to the root EnemyTeamIconPrefab and assign the visual child you want to bob
/// (usually StarImage, or another visual root if preferred).
/// </summary>
public class EnemyTeamIconBob : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private RectTransform bobTarget;

    [Header("Motion")]
    [SerializeField] private float bobAmount = 6f;
    [SerializeField] private float bobSpeed = 2f;
    [SerializeField] private bool useUnscaledTime = true;

    private Vector3 _baseLocalPosition;
    private bool _capturedBasePosition;

    private void Awake()
    {
        CaptureBasePosition();
    }

    private void OnEnable()
    {
        CaptureBasePosition();

        if (bobTarget != null)
            bobTarget.localPosition = _baseLocalPosition;
    }

    private void OnDisable()
    {
        if (bobTarget != null)
            bobTarget.localPosition = _baseLocalPosition;
    }

    private void LateUpdate()
    {
        if (bobTarget == null)
            return;

        if (!_capturedBasePosition)
            CaptureBasePosition();

        float t = useUnscaledTime ? Time.unscaledTime : Time.time;
        float yOffset = Mathf.Sin(t * bobSpeed) * bobAmount;

        Vector3 p = _baseLocalPosition;
        p.y += yOffset;
        bobTarget.localPosition = p;
    }

    private void CaptureBasePosition()
    {
        if (bobTarget == null)
            return;

        _baseLocalPosition = bobTarget.localPosition;
        _capturedBasePosition = true;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (bobAmount < 0f) bobAmount = 0f;
        if (bobSpeed < 0f) bobSpeed = 0f;
    }
#endif
}
