using UnityEngine;

/// <summary>
/// Put this on the player (or a persistent player controller object).
///
/// Press the resupply key near an ACTIVE ally that has AllyAmmoSupplyDonor.
/// The nearest eligible ally donates once, gives the player ammo, and is
/// downgraded to pistol behavior for the rest of its life.
///
/// Prompt support:
/// - Uses AmmoSupplyPromptUI if present in the scene.
/// - Does not use RecruitPromptUI, so the ammo prompt stays separate from the recruit prompt.
/// </summary>
[DisallowMultipleComponent]
public class PlayerAmmoResupplyFromAlly : MonoBehaviour
{
    [Header("Input")]
    public KeyCode resupplyKey = KeyCode.T;

    [Header("Range")]
    [Tooltip("How close the player must be to an eligible ally to take ammo.")]
    public float resupplyRange = 3f;

    [Header("Player Ammo Refs")]
    [Tooltip("Player rifle WeaponAmmo component.")]
    public WeaponAmmo rifleAmmo;

    [Tooltip("Player pistol WeaponAmmo component.")]
    public WeaponAmmo pistolAmmo;

    [Header("Ammo Granted Per Donation")]
    public int rifleRoundsGranted = 30;
    public int pistolRoundsGranted = 6;

    [Header("Player Ref")]
    [Tooltip("Optional explicit player transform. If empty, uses this transform.")]
    public Transform playerTransform;

    [Header("Prompt")]
    public bool showPrompt = true;
    public string promptText = "Press {KEY} to take ammo";

    private void Awake()
    {
        if (playerTransform == null) playerTransform = transform;
        AmmoSupplyPromptUI.Hide();
    }

    private void OnDisable()
    {
        HidePrompt();
    }

    private void Update()
    {
        if (playerTransform == null) playerTransform = transform;

        AllyAmmoSupplyDonor donor = FindNearestEligibleDonor();

        if (showPrompt)
        {
            if (donor != null)
            {
                string msg = string.IsNullOrWhiteSpace(promptText) ? "Press {KEY} to take ammo" : promptText;
                ShowPrompt(msg.Replace("{KEY}", resupplyKey.ToString()));
            }
            else
            {
                HidePrompt();
            }
        }
        else
        {
            HidePrompt();
        }

        if (donor == null) return;
        if (!Input.GetKeyDown(resupplyKey)) return;

        donor.TryDonateToPlayer(rifleAmmo, pistolAmmo, rifleRoundsGranted, pistolRoundsGranted);
        HidePrompt();
    }

    private AllyAmmoSupplyDonor FindNearestEligibleDonor()
    {
        AllyAmmoSupplyDonor[] donors = FindObjectsOfType<AllyAmmoSupplyDonor>();
        if (donors == null || donors.Length == 0) return null;

        AllyAmmoSupplyDonor best = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < donors.Length; i++)
        {
            AllyAmmoSupplyDonor donor = donors[i];
            if (donor == null) continue;
            if (!donor.IsEligibleForDonation(playerTransform, resupplyRange)) continue;

            float d = Vector3.Distance(playerTransform.position, donor.transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = donor;
            }
        }

        return best;
    }

    private void ShowPrompt(string message)
    {
        AmmoSupplyPromptUI.Show(message);
    }

    private void HidePrompt()
    {
        AmmoSupplyPromptUI.Hide();
    }
}
