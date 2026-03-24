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

    [Header("Protection Targets")]
    [Tooltip("If true, this ally will also defend itself when an animal/boss is targeting this ally (useful for followers).")]
    public bool protectThisAllyToo = true;

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

    [Header("Protective Fire Assist")]
    [Tooltip("If true, this add-on will directly fire the ally's normal bullet prefab at an active animal/boss threat when the threat is targeting the player. This keeps AllyController untouched.")]
    public bool enableProtectiveFireAssist = true;

    [Tooltip("Maximum distance allowed for protective assist shots. Set 0 to ignore.")]
    public float protectiveAssistMaxDistance = 40f;

    [Tooltip("If true, the add-on requires a clear line of sight from the ally firePoint before spawning a protective shot.")]
    public bool requireAssistLineOfSight = false;

    [Tooltip("Layers used by the optional protective fire LOS check. If you leave this as Everything, it will behave similarly to a broad obstruction check.")]
    public LayerMask assistLineOfSightMask = ~0;

    private float _nextScanTime;
    private float _nextRetargetTime;
    private float _nextAssistShotTime;
    private Transform _activeProtectThreat;

    // Cached reflection for AnimalController._target
    private static FieldInfo _animalTargetField;
    private static bool _animalReflectionReady;

    // Cached reflection for AlienBossController.CurrentTarget (property) or _target (field)
    private static PropertyInfo _bossCurrentTargetProp;
    private static FieldInfo _bossTargetField;
    private static bool _bossReflectionReady;

    // Cached reflection for optional AllyController private helpers / private combatStats field.
    private static MethodInfo _allyTriggerShotSoundMethod;
    private static MethodInfo _allyTriggerMuzzleFlashMethod;
    private static FieldInfo _allyCombatStatsField;
    private static MethodInfo _combatStatsGetDamageIntMethod;
    private static bool _allyReflectionReady;

    private readonly Collider[] _hits = new Collider[32];

    private void Awake()
    {
        if (!ally) ally = GetComponentInParent<AllyController>();
        if (!player) player = FindPlayer();

        PrepareAnimalReflection();
        PrepareBossReflection();
        PrepareAllyReflection();
    }

    private void OnEnable()
    {
        _nextScanTime = Time.time + UnityEngine.Random.Range(0f, scanInterval);
        _nextRetargetTime = -1f;
        _nextAssistShotTime = -1f;
        _activeProtectThreat = null;
    }

    private void Update()
    {
        if (!ally) return;

        if (!player)
            player = FindPlayer();

        if (Time.time >= _nextScanTime)
        {
            _nextScanTime = Time.time + Mathf.Max(0.05f, scanInterval);
            TryProtectPlayer();
        }

        TryProtectiveFireAssist();
    }

    private Transform FindPlayer()
    {
        if (string.IsNullOrEmpty(playerTag)) return null;
        var go = FindWithTagSafe(playerTag);
        return go ? go.transform : null;
    }

    private void PrepareAnimalReflection()
    {
        if (_animalReflectionReady) return;
        _animalReflectionReady = true;

        var t = Type.GetType("AnimalController");
        if (t == null)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType("AnimalController");
                if (t != null) break;
            }
        }

        if (t != null)
            _animalTargetField = t.GetField("_target", BindingFlags.Instance | BindingFlags.NonPublic);
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

    private void PrepareAllyReflection()
    {
        if (_allyReflectionReady) return;
        _allyReflectionReady = true;

        Type allyType = typeof(AllyController);
        _allyTriggerShotSoundMethod = allyType.GetMethod("TriggerShotSound", BindingFlags.Instance | BindingFlags.NonPublic);
        _allyTriggerMuzzleFlashMethod = allyType.GetMethod("TriggerMuzzleFlashSimple", BindingFlags.Instance | BindingFlags.NonPublic);
        _allyCombatStatsField = allyType.GetField("combatStats", BindingFlags.Instance | BindingFlags.NonPublic);

        if (_allyCombatStatsField != null)
        {
            Type combatStatsType = _allyCombatStatsField.FieldType;
            _combatStatsGetDamageIntMethod = combatStatsType.GetMethod("GetDamageInt", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
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
        if (!player && !protectThisAllyToo) return;

        if (!overrideExistingTarget && ally.CurrentEnemy != null)
            return;

        Transform bestThreat = FindBestThreateningAnimalOrBoss();

        if (!bestThreat)
        {
            if (_activeProtectThreat != null && !IsThreatStillProtectingRelevant(_activeProtectThreat))
                _activeProtectThreat = null;
            return;
        }

        _activeProtectThreat = bestThreat;

        if (Time.time < _nextRetargetTime) return;
        if (ally.CurrentEnemy == bestThreat) return;

        _nextRetargetTime = Time.time + Mathf.Max(0.05f, retargetCooldown);
        ally.ForceCombatTarget(bestThreat);

        if (logDecisions)
            Debug.Log($"[AllyProtectPlayerFromAnimals] {name} engaging threat '{bestThreat.name}' because it is targeting the player or this ally.", this);
    }

    private Transform FindBestThreateningAnimalOrBoss()
    {
        Vector3 origin = transform.position;

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

            if (root == transform || root.IsChildOf(transform))
                continue;

            var animalController = root.GetComponentInParent(Type.GetType("AnimalController") ?? GetAnimalControllerType());

            Component bossController = null;
            if (protectFromBosses)
                bossController = root.GetComponentInParent(Type.GetType("AlienBossController") ?? GetBossControllerType());

            bool isAnimalCandidate = animalController != null || (useTagFallback && !string.IsNullOrEmpty(animalTag) && SafeCompareTag(root, animalTag));
            bool isBossCandidate = protectFromBosses && (bossController != null || (!string.IsNullOrEmpty(bossTag) && SafeCompareTag(root, bossTag)));

            if (!isAnimalCandidate && !isBossCandidate)
                continue;

            Transform threatTarget = null;

            if (isBossCandidate && bossController != null)
                threatTarget = GetBossTarget(bossController);

            if (threatTarget == null && isAnimalCandidate && animalController != null)
                threatTarget = GetAnimalTarget(animalController);

            if (!IsThreatTargetRelevant(threatTarget))
                continue;

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

        if (!best && useTagFallback && !string.IsNullOrEmpty(animalTag))
        {
            GameObject[] tagged = FindGameObjectsWithTagSafe(animalTag);
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

                    if (!IsThreatTargetRelevant(animalTarget)) continue;

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

        if (!best && protectFromBosses && !string.IsNullOrEmpty(bossTag))
        {
            GameObject[] tagged = FindGameObjectsWithTagSafe(bossTag);
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

                    if (!IsThreatTargetRelevant(bossTarget)) continue;

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

        if (_animalTargetField == null)
        {
            PrepareAnimalReflection();
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

    private bool SafeCompareTag(Transform t, string tagToCheck)
    {
        if (!t || string.IsNullOrEmpty(tagToCheck)) return false;

        try
        {
            string currentTag = t.tag;
            return string.Equals(currentTag, tagToCheck, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static GameObject FindWithTagSafe(string tagToFind)
    {
        if (string.IsNullOrEmpty(tagToFind)) return null;
        try { return GameObject.FindGameObjectWithTag(tagToFind); }
        catch { return null; }
    }

    private static GameObject[] FindGameObjectsWithTagSafe(string tagToFind)
    {
        if (string.IsNullOrEmpty(tagToFind)) return null;
        try { return GameObject.FindGameObjectsWithTag(tagToFind); }
        catch { return null; }
    }

    private void TryProtectiveFireAssist()
    {
        if (!enableProtectiveFireAssist) return;
        if (!ally) return;
        if (!player && !protectThisAllyToo) return;
        if (ally.bullet == null || ally.firePoint == null) return;
        if (Time.time < _nextAssistShotTime) return;

        Transform threat = _activeProtectThreat;
        if (!IsThreatStillProtectingRelevant(threat))
            threat = FindBestThreateningAnimalOrBoss();

        if (!IsThreatStillProtectingRelevant(threat))
        {
            _activeProtectThreat = null;
            return;
        }

        _activeProtectThreat = threat;

        if (ally.CurrentEnemy != threat && Time.time >= _nextRetargetTime)
        {
            _nextRetargetTime = Time.time + Mathf.Max(0.05f, retargetCooldown);
            ally.ForceCombatTarget(threat);
        }

        Vector3 targetPoint = GetThreatAimPosition(threat);
        float dist = Vector3.Distance(ally.transform.position, targetPoint);
        if (protectiveAssistMaxDistance > 0f && dist > protectiveAssistMaxDistance) return;

        if (requireAssistLineOfSight && !HasAssistLineOfSight(targetPoint, threat))
            return;

        Vector3 flatDir = targetPoint - ally.transform.position;
        flatDir.y = 0f;
        if (flatDir.sqrMagnitude > 0.001f)
            ally.transform.rotation = Quaternion.LookRotation(flatDir.normalized, Vector3.up);

        Vector3 fireDir = targetPoint - ally.firePoint.position;
        if (fireDir.sqrMagnitude < 0.0001f) return;
        ally.firePoint.rotation = Quaternion.LookRotation(fireDir.normalized, Vector3.up);

        _nextAssistShotTime = Time.time + Mathf.Max(0.05f, ally.fireRate);

        GameObject spawned = Instantiate(ally.bullet, ally.firePoint.position, ally.firePoint.rotation);
        var bc = spawned != null ? spawned.GetComponent<BulletController>() : null;
        if (bc != null)
        {
            int dmg;
            if (TryGetAllyDamageInt(out dmg))
            {
                bc.Damage = dmg;
                bc.baseDamage = dmg;
            }

            bc.owner = ally.transform;
            bc.damageEnemy = true;
            bc.damageAlly = false;
            bc.damageAnimals = true;
            bc.damageBoss = true;
        }

        TriggerAllyShotPresentation();

        if (ally.soldierAnimator != null)
        {
            try { ally.soldierAnimator.SetTrigger("Shoot"); }
            catch { }
        }

        if (logDecisions)
            Debug.Log($"[AllyProtectPlayerFromAnimals] {name} fired protective assist shot at '{threat.name}'.", this);
    }

    private bool IsThreatStillProtectingRelevant(Transform threat)
    {
        if (!threat) return false;
        if (!threat.gameObject.activeInHierarchy) return false;
        if (!player) return false;

        Transform currentTarget = GetThreatCurrentTarget(threat);
        if (!IsThreatTargetRelevant(currentTarget)) return false;

        float d = Vector3.Distance(transform.position, threat.position);
        if (d > scanRadius + 2f) return false;

        if (requireAnimalNearPlayerDistance > 0f)
        {
            float dp = Vector3.Distance(threat.position, player.position);
            if (dp > requireAnimalNearPlayerDistance)
                return false;
        }

        return true;
    }

    private bool IsThreatTargetRelevant(Transform currentTarget)
    {
        if (!currentTarget) return false;

        if (player != null)
        {
            if (currentTarget == player || currentTarget.IsChildOf(player) || SafeCompareTag(currentTarget, playerTag))
                return true;
        }

        if (protectThisAllyToo && ally != null)
        {
            Transform allyRoot = ally.transform;
            if (currentTarget == allyRoot || currentTarget.IsChildOf(allyRoot))
                return true;
        }

        return false;
    }

    private Transform GetThreatCurrentTarget(Transform threatRoot)
    {
        if (!threatRoot) return null;

        var animalType = Type.GetType("AnimalController") ?? GetAnimalControllerType();
        if (animalType != null)
        {
            var animalController = threatRoot.GetComponentInParent(animalType);
            var animalTarget = GetAnimalTarget(animalController);
            if (animalTarget != null) return animalTarget;
        }

        if (protectFromBosses)
        {
            var bossType = Type.GetType("AlienBossController") ?? GetBossControllerType();
            if (bossType != null)
            {
                var bossController = threatRoot.GetComponentInParent(bossType);
                var bossTarget = GetBossTarget(bossController);
                if (bossTarget != null) return bossTarget;
            }
        }

        return null;
    }

    private Vector3 GetThreatAimPosition(Transform threat)
    {
        if (!threat) return transform.position + (Vector3.up * 1.2f);

        if (!string.IsNullOrWhiteSpace(ally.aimPointChildName))
        {
            Transform aimChild = threat.Find(ally.aimPointChildName);
            if (aimChild != null) return aimChild.position;
        }

        if (ally.useColliderBoundsForAim)
        {
            Collider c = threat.GetComponentInChildren<Collider>();
            if (c != null) return c.bounds.center;
        }

        return threat.position + (Vector3.up * ally.aimHeightOffset);
    }

    private bool HasAssistLineOfSight(Vector3 targetPoint, Transform threat)
    {
        if (!ally || ally.firePoint == null) return false;

        Vector3 losFrom = ally.firePoint.position + ally.firePoint.forward * Mathf.Max(0f, ally.lineOfSightFirePointForwardOffset);
        Vector3 losDir = targetPoint - losFrom;
        float losDist = losDir.magnitude;
        if (losDist <= 0.05f) return true;

        losDir /= losDist;
        if (Physics.Raycast(losFrom, losDir, out RaycastHit hit, losDist, assistLineOfSightMask, QueryTriggerInteraction.Ignore))
            return hit.transform == threat || hit.transform.IsChildOf(threat);

        return false;
    }

    private bool TryGetAllyDamageInt(out int damage)
    {
        damage = 0;
        PrepareAllyReflection();

        if (ally == null || _allyCombatStatsField == null || _combatStatsGetDamageIntMethod == null)
            return false;

        try
        {
            object stats = _allyCombatStatsField.GetValue(ally);
            if (stats == null) return false;

            object value = _combatStatsGetDamageIntMethod.Invoke(stats, null);
            if (value is int dmg)
            {
                damage = dmg;
                return true;
            }

            if (value is float dmgf)
            {
                damage = Mathf.RoundToInt(dmgf);
                return true;
            }
        }
        catch { }

        return false;
    }

    private void TriggerAllyShotPresentation()
    {
        PrepareAllyReflection();

        try { _allyTriggerMuzzleFlashMethod?.Invoke(ally, null); }
        catch { }

        try { _allyTriggerShotSoundMethod?.Invoke(ally, null); }
        catch { }
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;
        Gizmos.DrawWireSphere(transform.position, scanRadius);
    }
}
