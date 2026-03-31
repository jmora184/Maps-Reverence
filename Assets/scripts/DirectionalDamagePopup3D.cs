using UnityEngine;

/// <summary>
/// Small floating world-space text used for directional damage popups like "2x".
/// Safe default: if you do not assign a prefab, EnemyHealthController can create this at runtime.
/// </summary>
public class DirectionalDamagePopup3D : MonoBehaviour
{
    [Header("Visual")]
    public TextMesh textMesh;
    public Color textColor = new Color(1f, 0.85f, 0.1f, 1f);
    public int fontSize = 64;
    public float characterSize = 0.08f;
    public FontStyle fontStyle = FontStyle.Bold;
    public string popupText = "2x";

    [Header("Motion")]
    public float lifetime = 0.85f;
    public float riseSpeed = 0.9f;
    public float sidewaysDrift = 0.2f;
    public bool faceMainCamera = true;

    private float _age;
    private Vector3 _driftDir;

    public static DirectionalDamagePopup3D CreateRuntime(Vector3 position)
    {
        var go = new GameObject("DirectionalDamagePopup3D");
        go.transform.position = position;
        return go.AddComponent<DirectionalDamagePopup3D>();
    }

    private void Awake()
    {
        EnsureTextMesh();
        _driftDir = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-0.2f, 0.2f));
        if (_driftDir.sqrMagnitude <= 0.0001f)
            _driftDir = Vector3.right;
        _driftDir.Normalize();
        ApplyVisuals();
    }

    public void SetText(string value)
    {
        popupText = string.IsNullOrEmpty(value) ? "2x" : value;
        EnsureTextMesh();
        textMesh.text = popupText;
    }

    public void SetColor(Color color)
    {
        textColor = color;
        EnsureTextMesh();
        textMesh.color = textColor;
    }

    private void Update()
    {
        _age += Time.deltaTime;
        transform.position += (Vector3.up * riseSpeed + _driftDir * sidewaysDrift) * Time.deltaTime;

        if (faceMainCamera && Camera.main != null)
        {
            Vector3 toCam = transform.position - Camera.main.transform.position;
            if (toCam.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(toCam.normalized, Vector3.up);
        }

        if (textMesh != null && lifetime > 0f)
        {
            float alpha = 1f - Mathf.Clamp01(_age / lifetime);
            Color c = textColor;
            c.a = alpha;
            textMesh.color = c;
        }

        if (_age >= lifetime)
            Destroy(gameObject);
    }

    private void EnsureTextMesh()
    {
        if (textMesh == null)
            textMesh = GetComponent<TextMesh>();

        if (textMesh == null)
            textMesh = gameObject.AddComponent<TextMesh>();
    }

    private void ApplyVisuals()
    {
        EnsureTextMesh();
        textMesh.text = popupText;
        textMesh.color = textColor;
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.fontSize = fontSize;
        textMesh.characterSize = characterSize;
        textMesh.fontStyle = fontStyle;

        var renderer = textMesh.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }
}
