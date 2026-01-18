using UnityEngine;
using TMPro;

/// <summary>
/// Optional label controller for destination markers.
/// Put a TextMeshPro (3D) component on a child object of the marker prefab and assign it here.
/// NOTE: Use TextMeshPro (3D), not TextMeshProUGUI.
/// </summary>
public class DestinationMarkerLabel : MonoBehaviour
{
    [Header("Assign a TextMeshPro (3D) component")]
    public TMP_Text tmp;

    public void SetText(string value)
    {
        if (tmp != null)
            tmp.text = value ?? string.Empty;
    }
}
