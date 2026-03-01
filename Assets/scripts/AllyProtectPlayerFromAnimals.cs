using System;
using System.Reflection;
using UnityEngine;

/// <summary>
/// AllyProtectPlayerFromAnimals
/// - Add-on script (does NOT modify AllyController).
/// - Periodically scans for nearby animals. If an animal's CURRENT TARGET is the Player,
///   the ally will engage that animal (ForceCombatTarget).
///
/// Requirements:
/// - AllyController must exist on the same GameObject (or parent).
/// - AnimalController must exist on the animal root (or parent of the collider).
/// - This reads AnimalController's private field "_target" via reflection (safe + cached).
///
/// Tips:
/// - Ensure animals are on layers included by animalScanMask, OR use useTagFallback = true.
/// </summary>
[DisallowMultipleComponent]
public class AllyProtectPlayerFromAnimals : MonoBehaviour
{
    [Header("References")]
    public AllyController ally;

    [Tooltip("Optional explicit player transform. If null, we will find by playerTag.")]
    public Transform player;

    [Tooltip("Tag used to find the player when Player is not assigned.")]
    public string playerTag = "Player";

    [Header("Scan")]
    [Tooltip("How far this ally will look for animals threatening the player.")]
    public float scanRadius = 30f;

    [Tooltip("Seconds between scans (lower = more responsive, higher = cheaper).")]
    public float scanInterval = 0.35f;

    [Tooltip("Which layers are considered 'animals' for scanning. If unsure, leave Everything.")]
    public LayerMask animalScanMask = ~0;

    [Tooltip("If true, will also consider objects tagged animalTag even if layer mask misses them.")]
    public bool useTagFallback = true;

    [Tooltip("Tag used for animals (only used if Use Tag Fallback is true).")]
    public string animalTag = "Animal";

    [Header("Boss (AlienBoss) Scan")]
    [Tooltip("If true, allies will also protect the player from AlienBosses when the boss is targeting the player.")]
    public bool protectFromBosses = true;

    [Tooltip("Tag used for bosses (AlienBoss root).")]
    public string bossTag = "Boss";

    [Tooltip("Which layers are considered bosses for scanning. If unsure, leave Everything.")] 
    public LayerMask bossScanMask = ~0;
    [Header("Engagement Rules")]
    [Tooltip("If true, the ally will switch targets to protect the player even if currently fighting something else.")]
    public bool overrideExistingTarget = true;

    [Tooltip("Minimum seconds between calling ForceCombatTarget (prevents spam).")]
    public float retargetCooldown = 0.6f;

    [Tooltip("If true, the ally will only engage animals that are within scanRadius AND also within this distance of the player. Set 0 to disable.")]
    public float requireAnimalNearPlayerDistance = 0f;

    [Header("Debug")]
    public bool logDecisions = false;
    public bool drawGizmos = true;

    private float _nextScanTime;
    private float _nextRetargetTime;

    // Cached reflection for AnimalController._target
    private static FieldInfo _animalTargetField;
    private static bool _animalReflectionReady;


    // Cached reflection for AlienBossController.CurrentTarget (property) or _target (field)
    private static PropertyInfo _bossCurrentTargetProp;
    private static FieldInfo _bossTargetField;
    private static bool _bossReflectionReady;
    private readonly Collider[] _hits = new Collider[32];

    private void Awake()
    {
        if (!ally) ally = GetComponentInParent<AllyController>();
        if (!player) player = FindPlayer();

        PrepareAnimalReflection();
        PrepareBossReflection();
    }

    private void OnEnable()
    {
        _nextScanTime = Time.time + UnityEngine.Random.Range(0f, scanInterval);
        _nextRetargetTime = -1f;
    }

    private void Update()
    {
        if (!ally) return;

        if (!player)
            player = FindPlayer();

        if (Time.time < _nextScanTime) return;
        _nextScanTime = Time.time + Mathf.Max(0.05f, scanInterval);

        TryProtectPlayer();
    }

    private Transform FindPlayer()
    {
        if (string.IsNullOrEmpty(playerTag)) return null;
        var go = GameObject.FindGameObjectWithTag(playerTag);
        return go ? go.transform : null;
    }

    private void PrepareAnimalReflection()
    {
        if (_animalReflectionReady) return;
        _animalReflectionReady = true;

        // Find AnimalController type by name (no hard dependency on the file name/path).
        // Assumes class name is AnimalController.
        var t = Type.GetType("AnimalController");
        if (t == null)
        {
            // Unity assemblies: try searching loaded assemblies
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType("AnimalController");
                if (t != null) break;
            }
        }

        if (t != null)
        {
            _animalTargetField = t.GetField("_target", BindingFlags.Instance | BindingFlags.NonPublic);
        }
    }

    private void PrepareBossReflection()
    {
        if (_bossReflectionReady) return;
        _bossReflectionReady = true;

        var t = Type.GetType("AlienBossController");
        if (t == null)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType("AlienBossController");
                if (t != null) break;
            }
        }

        if (t != null)
        {
            _bossCurrentTargetProp = t.GetProperty("CurrentTarget", BindingFlags.Instance | BindingFlags.Public);
            _bossTargetField = t.GetField("_target", BindingFlags.Instance | BindingFlags.NonPublic);
        }
    }

    private Transform GetBossTarget(Component bossController)
    {
        if (bossController == null) return null;

        if (_bossCurrentTargetProp == null && _bossTargetField == null)
        {
            PrepareBossReflection();
            if (_bossCurrentTargetProp == null && _bossTargetField == null) return null;
        }

        try
        {
            if (_bossCurrentTargetProp != null)
            {
                object val = _bossCurrentTargetProp.GetValue(bossController, null);
                return val as Transform;
            }

            if (_bossTargetField != null)
            {
                object val = _bossTargetField.GetValue(bossController);
                return val as Transform;
            }
        }
        catch { }

        return null;
    }

    private static Type GetBossControllerType()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType("AlienBossController");
            if (t != null) return t;
        }
        return null;
    }


    private void TryProtectPlayer()
    {
        if (!player) return;

        // If we shouldn't override and we're already fighting, do nothing.
        if (!overrideExistingTarget && ally.CurrentEnemy != null)
            return;

        Transform bestAnimal = FindBestThreateningAnimalOrBoss();
        if (!bestAnimal) return;

        // Avoid spamming ForceCombatTarget.
        if (Time.time < _nextRetargetTime) return;

        // If we're already targeting this animal, skip.
        if (ally.CurrentEnemy == bestAnimal) return;

        _nextRetargetTime = Time.time + Mathf.Max(0.05f, retargetCooldown);
        ally.ForceCombatTarget(bestAnimal);

        if (logDecisions)
            Debug.Log($"[AllyProtectPlayerFromAnimals] {name} engaging threat '{bestAnimal.name}' because it is targeting the player.", this);
    }

    private Transform FindBestThreateningAnimalOrBoss()
    {
        Vector3 origin = transform.position;

        // Scan using combined masks (animals + bosses) so you can tune them separately.
        LayerMask mask = animalScanMask;
        if (protectFromBosses) mask |= bossScanMask;
        int count = Physics.OverlapSphereNonAlloc(origin, scanRadius, _hits, mask, QueryTriggerInteraction.Ignore);

        Transform best = null;
        float bestD = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            var c = _hits[i];
            if (!c) continue;

            Transform root = c.transform.root != null ? c.transform.root : c.transform;

            // Skip self
            if (root == transform || root.IsChildOf(transform)) continue;

            // Check for AnimalController (Animal threat)
            var animalController = root.GetComponentInParent(Type.GetType("AnimalController") ?? GetAnimalControllerType());

            // Check for AlienBossController (Boss threat)
            Component bossController = null;
            if (protectFromBosses)
                bossController = root.GetComponentInParent(Type.GetType("AlienBossController") ?? GetBossControllerType());

            bool isAnimalCandidate = animalController != null || (useTagFallback && !string.IsNullOrEmpty(animalTag) && SafeCompareTag(root, animalTag));
            bool isBossCandidate = protectFromBosses && (bossController != null || (!string.IsNullOrEmpty(bossTag) && SafeCompareTag(root, bossTag)));

            if (!isAnimalCandidate && !isBossCandidate)
                continue;

            // If tag fallback is enabled, still enforce tag when present.
            if (useTagFallback && !string.IsNullOrEmpty(animalTag))
            {
                // If your animals are not tagged, leave animalTag empty or disable tag fallback.
                if (!string.IsNullOrEmpty(animalTag) && root.tag != animalTag)
                {
                    // don't hard-fail if animalTag isn't defined in Tag Manager (Unity throws); so we keep it soft:
                    // only compare when it looks valid.
                }
            }

            // Determine current target (animal or boss).
            Transform threatTarget = null;

            if (isBossCandidate && bossController != null)
                threatTarget = GetBossTarget(bossController);

            if (threatTarget == null && isAnimalCandidate && animalController != null)
                threatTarget = GetAnimalTarget(animalController);

            // If we only matched by tag and have no controller, we cannot know target.
            if (threatTarget == null) continue;

            // Must be targeting the player
            if (threatTarget != player && !SafeCompareTag(threatTarget, playerTag))
                continue;

            // Optional: only if animal is near player too
            if (requireAnimalNearPlayerDistance > 0f)
            {
                float dp = Vector3.Distance(root.position, player.position);
                if (dp > requireAnimalNearPlayerDistance)
                    continue;
            }

            float d = Vector3.Distance(origin, root.position);
            if (d < bestD)
            {
                bestD = d;
                best = root;
            }
        }

        // Tag fallback if layer scan missed everything (common if mask excludes animals)
        if (!best && useTagFallback && !string.IsNullOrEmpty(animalTag))
        {
            GameObject[] tagged = null;
            try { tagged = GameObject.FindGameObjectsWithTag(animalTag); }
            catch { /* tag might not exist */ }

            if (tagged != null)
            {
                foreach (var go in tagged)
                {
                    if (!go) continue;
                    Transform root = go.transform.root != null ? go.transform.root : go.transform;

                    float d = Vector3.Distance(origin, root.position);
                    if (d > scanRadius) continue;

                    var animalController = root.GetComponentInParent(Type.GetType("AnimalController") ?? GetAnimalControllerType());
                    Transform animalTarget = GetAnimalTarget(animalController);

                    if (animalTarget == null) continue;
                    if (animalTarget != player && !animalTarget.CompareTag(playerTag)) continue;

                    if (requireAnimalNearPlayerDistance > 0f)
                    {
                        float dp = Vector3.Distance(root.position, player.position);
                        if (dp > requireAnimalNearPlayerDistance)
                            continue;
                    }

                    if (d < bestD)
                    {
                        bestD = d;
                        best = root;
                    }
                }
            }
        }


        // Boss tag fallback (if layer scan missed everything)
        if (!best && protectFromBosses && !string.IsNullOrEmpty(bossTag))
        {
            GameObject[] tagged = null;
            try { tagged = GameObject.FindGameObjectsWithTag(bossTag); }
            catch { /* tag might not exist */ }

            if (tagged != null)
            {
                foreach (var go in tagged)
                {
                    if (!go) continue;
                    Transform root = go.transform.root != null ? go.transform.root : go.transform;

                    float d = Vector3.Distance(origin, root.position);
                    if (d > scanRadius) continue;

                    var bossController = root.GetComponentInParent(Type.GetType("AlienBossController") ?? GetBossControllerType());
                    Transform bossTarget = GetBossTarget(bossController);

                    if (bossTarget == null) continue;
                    if (bossTarget != player && !SafeCompareTag(bossTarget, playerTag)) continue;

                    if (requireAnimalNearPlayerDistance > 0f)
                    {
                        float dp = Vector3.Distance(root.position, player.position);
                        if (dp > requireAnimalNearPlayerDistance)
                            continue;
                    }

                    if (d < bestD)
                    {
                        bestD = d;
                        best = root;
                    }
                }
            }
        }

        return best;
    }

    private static Type GetAnimalControllerType()
    {
        // Try locate by scanning assemblies once.
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType("AnimalController");
            if (t != null) return t;
        }
        return null;
    }

    private Transform GetAnimalTarget(Component animalController)
    {
        if (animalController == null) return null;

        // Use cached FieldInfo if we found it
        if (_animalTargetField == null)
        {
            PrepareAnimalReflection();
        PrepareBossReflection();
            if (_animalTargetField == null) return null;
        }

        try
        {
            object val = _animalTargetField.GetValue(animalController);
            return val as Transform;
        }
        catch
        {
            return null;
        }
    }



    private bool SafeCompareTag(Transform t, string tag)
    {
        if (!t || string.IsNullOrEmpty(tag)) return false;
        try { return t.CompareTag(tag); }
        catch { return false; }
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;
        Gizmos.DrawWireSphere(transform.position, scanRadius);
    }
}