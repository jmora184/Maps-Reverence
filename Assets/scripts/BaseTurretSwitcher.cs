using UnityEngine;

/// <summary>
/// Simple scene-object switcher for base capture.
///
/// Use this when a base changes ownership and you want to:
/// - disable enemy turret GameObjects
/// - enable ally turret GameObjects
///
/// This does NOT modify turret behavior scripts.
/// It only toggles GameObject active state.
/// </summary>
public class BaseTurretSwitcher : MonoBehaviour
{
    [Header("Objects active before capture")]
    [SerializeField] private GameObject[] enemyTurrets;

    [Header("Objects active after capture")]
    [SerializeField] private GameObject[] allyTurrets;

    [Header("Optional")]
    [SerializeField] private bool runOnceOnly = true;

    private bool hasSwitched = false;

    private void Awake()
    {
        // Keep the scene in a clean default state.
        SetObjectsActive(enemyTurrets, true);
        SetObjectsActive(allyTurrets, false);
    }

    /// <summary>
    /// Call this when the player captures the base.
    /// </summary>
    public void SwitchToAlly()
    {
        if (runOnceOnly && hasSwitched)
            return;

        SetObjectsActive(enemyTurrets, false);
        SetObjectsActive(allyTurrets, true);

        hasSwitched = true;
    }

    /// <summary>
    /// Optional helper if you ever want to reset the base back to enemy control.
    /// </summary>
    public void SwitchToEnemy()
    {
        SetObjectsActive(enemyTurrets, true);
        SetObjectsActive(allyTurrets, false);

        hasSwitched = false;
    }

    private void SetObjectsActive(GameObject[] objects, bool isActive)
    {
        if (objects == null)
            return;

        for (int i = 0; i < objects.Length; i++)
        {
            if (objects[i] != null)
                objects[i].SetActive(isActive);
        }
    }
}
