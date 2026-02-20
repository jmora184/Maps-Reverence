using UnityEngine;

/// <summary>
/// Shows a simple on-screen greeting when the player is close to an NPC.
/// Designed to mirror your AllyActivationGate -> RecruitPromptUI pattern.
///
/// - No recruiting.
/// - Optional interact key that can fire BeginDialogue() via SendMessage (no hard dependency).
///
/// NOTE: This uses RecruitPromptUI.Show/Hide (your existing UI). If you stand in range of an
/// inactive ally AND an NPC at the same time, whichever script updates last will win.
/// </summary>
[DisallowMultipleComponent]
public class NPCGreetingPrompt : MonoBehaviour
{
    [Header("Greeting")]
    [SerializeField] private string greetingText = "Hello Captain";
    [SerializeField] private float greetingRange = 2.5f;

    [Header("Player")]
    [Tooltip("Optional explicit player Transform. If null, we search by tag.")]
    [SerializeField] private Transform playerOverride;
    [SerializeField] private string playerTag = "Player";

    [Header("Optional Interact")]
    [Tooltip("If enabled, pressing this key while in range will SendMessage('BeginDialogue') to this NPC.")]
    [SerializeField] private bool allowInteract = false;
    [SerializeField] private KeyCode interactKey = KeyCode.E;

    [Header("Display Rules")]
    [Tooltip("If true, hides the greeting while this NPC is hostile (weapon shown or you set this manually).")]
    [SerializeField] private bool hideWhenHostile = true;
    [Tooltip("If assigned, 'hostile' is considered true when this object is active.")]
    [SerializeField] private GameObject weaponObject;

    private Transform _player;
    private bool _showing;

    private void Awake()
    {
        ResolvePlayer();

        // Auto-find a common weapon child name if you didn't wire it.
        if (weaponObject == null)
        {
            var t = transform.Find("w_usp45");
            if (t != null) weaponObject = t.gameObject;
        }
    }

    private void OnEnable()
    {
        ResolvePlayer();
    }

    private void OnDisable()
    {
        // Only hide if we were the one showing.
        if (_showing)
        {
            RecruitPromptUI.Hide();
            _showing = false;
        }
    }

    private void Update()
    {
        if (_player == null) ResolvePlayer();
        if (_player == null) return;

        bool inRange = Vector3.Distance(_player.position, transform.position) <= greetingRange;
        bool hostileNow = hideWhenHostile && weaponObject != null && weaponObject.activeInHierarchy;

        if (inRange && !hostileNow)
        {
            if (!_showing)
            {
                RecruitPromptUI.Show(greetingText);
                _showing = true;
            }

            if (allowInteract && Input.GetKeyDown(interactKey))
            {
                // No hard dependency on NPCController.
                // If you later add BeginDialogue() (or already have it), this will call it.
                gameObject.SendMessage("BeginDialogue", SendMessageOptions.DontRequireReceiver);
            }
        }
        else
        {
            if (_showing)
            {
                RecruitPromptUI.Hide();
                _showing = false;
            }
        }
    }

    private void ResolvePlayer()
    {
        if (playerOverride != null)
        {
            _player = playerOverride;
            return;
        }

        GameObject playerObj = null;
        try { playerObj = GameObject.FindGameObjectWithTag(playerTag); } catch { }

        _player = (playerObj != null) ? playerObj.transform : null;
    }
}
