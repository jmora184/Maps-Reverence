using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-10000)]
public class MNRSaveSystem : MonoBehaviour
{
    [Serializable]
    private class SaveFileData
    {
        public string sceneName = "";
        public List<BaseState> bases = new List<BaseState>();
        public List<TeamState> teams = new List<TeamState>();
        public List<IndividualActorState> individuals = new List<IndividualActorState>();
    }

    [Serializable]
    private class BaseState
    {
        public string id = "";
        public bool captured = false;
    }

    [Serializable]
    private class TeamState
    {
        public string id = "";
        public bool hasSpawned = false;
        public bool isAlly = false;
        public List<MemberState> members = new List<MemberState>();
    }

    [Serializable]
    private class MemberState
    {
        public int slot = -1;
        public bool alive = false;
        public bool hasAmmoDonationState = false;
        public bool ammoDonated = false;
    }

    [Serializable]
    private class IndividualActorState
    {
        public string id = "";

        public bool hasLifeState = false;
        public bool alive = true;

        public bool hasTransform = false;
        public Vector3 position = Vector3.zero;
        public Quaternion rotation = Quaternion.identity;

        public bool hasActivationState = false;
        public bool activationActive = true;

        public bool hasPrisonerState = false;
        public bool prisoner = false;

        public bool hasEnemyWaypointPatrol = false;
        public bool enemyWaypointPatrolEnabled = false;

        public bool hasAllyPingPongPatrol = false;
        public bool allyPingPongPatrolEnabled = false;

        public bool hasAmmoDonationState = false;
        public bool ammoDonated = false;
    }

    private static MNRSaveSystem _instance;
    private static bool _continueLoadRequested;
    private Coroutine _loadRoutine;

    private static string SaveFilePath => Path.Combine(Application.persistentDataPath, "mnr_save.json");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        EnsureInstance();
    }

    public static bool HasSave()
    {
        return File.Exists(SaveFilePath);
    }

    public static string GetSavedSceneNameOrFallback(string fallbackSceneName)
    {
        var data = ReadSaveFile();
        if (data != null && !string.IsNullOrWhiteSpace(data.sceneName))
            return data.sceneName;

        return fallbackSceneName;
    }

    public static void DeleteSave()
    {
        if (File.Exists(SaveFilePath))
            File.Delete(SaveFilePath);
    }

    public static void RequestContinueLoad()
    {
        EnsureInstance();
        _continueLoadRequested = true;
    }

    public static bool SaveCurrentGame()
    {
        EnsureInstance();
        return _instance != null && _instance.InternalSaveCurrentGame();
    }

    private static void EnsureInstance()
    {
        if (_instance != null)
            return;

#if UNITY_2023_1_OR_NEWER
        var existing = FindFirstObjectByType<MNRSaveSystem>();
#else
        var existing = FindObjectOfType<MNRSaveSystem>();
#endif
        if (existing != null)
        {
            _instance = existing;
            return;
        }

        var go = new GameObject("MNRSaveSystem");
        _instance = go.AddComponent<MNRSaveSystem>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!_continueLoadRequested)
            return;

        if (_loadRoutine != null)
            StopCoroutine(_loadRoutine);

        _loadRoutine = StartCoroutine(ApplySavedGameAfterSceneLoad());
    }

    private bool InternalSaveCurrentGame()
    {
        try
        {
            SaveFileData data = BuildSaveData();
            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(SaveFilePath, json);
            Debug.Log($"[MNRSaveSystem] Saved game to {SaveFilePath}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MNRSaveSystem] Failed to save game: {ex.Message}");
            return false;
        }
    }

    private SaveFileData BuildSaveData()
    {
        var data = new SaveFileData
        {
            sceneName = SceneManager.GetActiveScene().name
        };

        CollectBaseStates(data);
        CollectTeamStates(data);
        CollectIndividualActorStates(data);
        return data;
    }

    private void CollectBaseStates(SaveFileData data)
    {
        var bases = FindObjectsByType<BaseCaptureController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < bases.Length; i++)
        {
            var controller = bases[i];
            if (controller == null) continue;
            if (string.IsNullOrWhiteSpace(controller.saveId)) continue;

            data.bases.Add(new BaseState
            {
                id = controller.saveId.Trim(),
                captured = controller.HasCaptured
            });
        }

        data.bases = data.bases.OrderBy(x => x.id, StringComparer.Ordinal).ToList();
    }

    private void CollectTeamStates(SaveFileData data)
    {
        var teamMap = new Dictionary<string, TeamState>(StringComparer.Ordinal);

        RegisterConfiguredLevelOnePlans(teamMap);
        RegisterConfiguredEncounterGroups(teamMap);
        RegisterRuntimeTeams(teamMap);

        data.teams = teamMap.Values.OrderBy(x => x.id, StringComparer.Ordinal).ToList();
    }

    private void RegisterConfiguredLevelOnePlans(Dictionary<string, TeamState> teamMap)
    {
        var allLevelOne = FindObjectsByType<LevelOne>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < allLevelOne.Length; i++)
        {
            var level = allLevelOne[i];
            if (level == null) continue;

            if (level.teams != null)
            {
                for (int t = 0; t < level.teams.Count; t++)
                {
                    var plan = level.teams[t];
                    if (plan == null || string.IsNullOrWhiteSpace(plan.saveId)) continue;
                    GetOrCreateTeamState(teamMap, plan.saveId, false);
                }
            }

            if (level.allyTeams != null)
            {
                for (int t = 0; t < level.allyTeams.Count; t++)
                {
                    var plan = level.allyTeams[t];
                    if (plan == null || string.IsNullOrWhiteSpace(plan.saveId)) continue;
                    GetOrCreateTeamState(teamMap, plan.saveId, true);
                }
            }
        }
    }

    private void RegisterConfiguredEncounterGroups(Dictionary<string, TeamState> teamMap)
    {
        var directors = FindObjectsByType<EncounterDirectorPOC>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < directors.Length; i++)
        {
            var director = directors[i];
            if (director == null) continue;

            RegisterEncounterGroupArray(teamMap, director.enemyGroups, false);
            RegisterEncounterGroupArray(teamMap, director.allyGroups, true);
        }
    }

    private static void RegisterEncounterGroupArray(Dictionary<string, TeamState> teamMap, EncounterDirectorPOC.SpawnGroup[] groups, bool isAlly)
    {
        if (groups == null)
            return;

        for (int i = 0; i < groups.Length; i++)
        {
            var group = groups[i];
            if (string.IsNullOrWhiteSpace(group.saveId))
                continue;

            GetOrCreateTeamState(teamMap, group.saveId, isAlly);
        }
    }

    private void RegisterRuntimeTeams(Dictionary<string, TeamState> teamMap)
    {
        var runtimes = FindObjectsByType<MNRSaveTeamRuntime>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < runtimes.Length; i++)
        {
            var runtime = runtimes[i];
            if (runtime == null || string.IsNullOrWhiteSpace(runtime.SaveId))
                continue;

            var state = GetOrCreateTeamState(teamMap, runtime.SaveId, runtime.IsAlly);
            state.hasSpawned = true;
            state.members.Clear();

            var members = runtime.GetComponentsInChildren<MNRSaveMemberRuntime>(true)
                .Where(m => m != null && string.Equals(m.TeamSaveId, runtime.SaveId, StringComparison.Ordinal))
                .OrderBy(m => m.MemberSlot)
                .ToArray();

            for (int m = 0; m < members.Length; m++)
            {
                var donor = FindAmmoSupplyDonor(members[m]);

                state.members.Add(new MemberState
                {
                    slot = members[m].MemberSlot,
                    alive = members[m].IsAliveForSave(),
                    hasAmmoDonationState = donor != null,
                    ammoDonated = donor != null && donor.HasDonatedAmmo
                });
            }
        }
    }

    private void CollectIndividualActorStates(SaveFileData data)
    {
        var actors = FindObjectsByType<MNRSaveIndividualActor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var usedIds = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < actors.Length; i++)
        {
            var actor = actors[i];
            if (actor == null)
                continue;

            string id = actor.SaveId;
            if (string.IsNullOrWhiteSpace(id))
                continue;

            if (!usedIds.Add(id))
            {
                Debug.LogWarning($"[MNRSaveSystem] Duplicate individual saveId '{id}' found. Later duplicate ignored.", actor);
                continue;
            }

            var state = new IndividualActorState
            {
                id = id
            };

            if (actor.SaveLifeState)
            {
                state.hasLifeState = true;
                state.alive = actor.IsAliveForSave();
            }

            if (actor.SaveTransform)
            {
                state.hasTransform = true;
                state.position = actor.transform.position;
                state.rotation = actor.transform.rotation;
            }

            if (actor.SaveActivationState)
            {
                var gate = actor.GetComponent<AllyActivationGate>();
                if (gate != null)
                {
                    state.hasActivationState = true;
                    state.activationActive = gate.IsActive;
                }
            }

            if (actor.SavePrisonerState)
            {
                var prisoner = actor.GetComponent<AllyPrisonerState>();
                if (prisoner != null)
                {
                    state.hasPrisonerState = true;
                    state.prisoner = prisoner.IsPrisoner;
                }
            }

            if (actor.SavePatrolState)
            {
                var enemyWaypointPatrol = actor.GetComponent<EnemyWaypointPatrol>();
                if (enemyWaypointPatrol != null)
                {
                    state.hasEnemyWaypointPatrol = true;
                    state.enemyWaypointPatrolEnabled = enemyWaypointPatrol.PatrolEnabled;
                }

                var allyPingPongPatrol = actor.GetComponent<AllyPatrolPingPong>();
                if (allyPingPongPatrol != null)
                {
                    state.hasAllyPingPongPatrol = true;
                    state.allyPingPongPatrolEnabled = allyPingPongPatrol.patrolEnabledOnStart;
                }
            }

            var donor = FindAmmoSupplyDonor(actor);
            if (donor != null)
            {
                state.hasAmmoDonationState = true;
                state.ammoDonated = donor.HasDonatedAmmo;
            }

            data.individuals.Add(state);
        }

        data.individuals = data.individuals.OrderBy(x => x.id, StringComparer.Ordinal).ToList();
    }

    private static TeamState GetOrCreateTeamState(Dictionary<string, TeamState> teamMap, string saveId, bool isAlly)
    {
        string key = saveId.Trim();
        if (!teamMap.TryGetValue(key, out var state) || state == null)
        {
            state = new TeamState { id = key, isAlly = isAlly, hasSpawned = false };
            teamMap[key] = state;
        }
        else
        {
            state.isAlly = isAlly;
        }

        return state;
    }

    private IEnumerator ApplySavedGameAfterSceneLoad()
    {
        yield return null;
        yield return null;

        var data = ReadSaveFile();
        if (data == null)
        {
            _continueLoadRequested = false;
            _loadRoutine = null;
            yield break;
        }

        string activeScene = SceneManager.GetActiveScene().name;
        if (!string.IsNullOrWhiteSpace(data.sceneName) && !string.Equals(activeScene, data.sceneName, StringComparison.Ordinal))
        {
            Debug.LogWarning($"[MNRSaveSystem] Continue requested, but loaded scene '{activeScene}' does not match save scene '{data.sceneName}'.");
            _continueLoadRequested = false;
            _loadRoutine = null;
            yield break;
        }

        ApplySavedBaseStates(data);
        yield return null;

        SpawnMissingSavedTeams(data);
        yield return null;

        ApplySavedTeamMemberStates(data);
        yield return null;

        ApplySavedIndividualActorStates(data);

        _continueLoadRequested = false;
        _loadRoutine = null;
    }

    private void ApplySavedBaseStates(SaveFileData data)
    {
        if (data == null || data.bases == null || data.bases.Count == 0)
            return;

        var bases = FindObjectsByType<BaseCaptureController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < bases.Length; i++)
        {
            var controller = bases[i];
            if (controller == null || string.IsNullOrWhiteSpace(controller.saveId))
                continue;

            for (int s = 0; s < data.bases.Count; s++)
            {
                var state = data.bases[s];
                if (state == null || !state.captured)
                    continue;

                if (string.Equals(controller.saveId.Trim(), state.id, StringComparison.Ordinal))
                {
                    controller.ApplySavedCapturedStateForLoad();
                    break;
                }
            }
        }
    }

    private void SpawnMissingSavedTeams(SaveFileData data)
    {
        if (data == null || data.teams == null || data.teams.Count == 0)
            return;

        for (int i = 0; i < data.teams.Count; i++)
        {
            var team = data.teams[i];
            if (team == null || string.IsNullOrWhiteSpace(team.id))
                continue;

            if (!team.hasSpawned)
                continue;

            if (!HasAnyAliveMembers(team))
                continue;

            if (FindRuntimeBySaveId(team.id) != null)
                continue;

            if (TrySpawnFromLevelOne(team.id))
                continue;

            TrySpawnFromEncounterDirector(team.id);
        }
    }

    private void ApplySavedTeamMemberStates(SaveFileData data)
    {
        if (data == null || data.teams == null || data.teams.Count == 0)
            return;

        for (int i = 0; i < data.teams.Count; i++)
        {
            var team = data.teams[i];
            if (team == null || string.IsNullOrWhiteSpace(team.id))
                continue;

            var runtime = FindRuntimeBySaveId(team.id);
            if (runtime == null)
                continue;

            var members = runtime.GetComponentsInChildren<MNRSaveMemberRuntime>(true);
            if (members == null || members.Length == 0)
                continue;

            var memberLookup = new Dictionary<int, MemberState>();
            for (int m = 0; m < team.members.Count; m++)
            {
                var memberState = team.members[m];
                if (memberState == null) continue;
                memberLookup[memberState.slot] = memberState;
            }

            for (int m = 0; m < members.Length; m++)
            {
                var member = members[m];
                if (member == null) continue;

                bool foundState = memberLookup.TryGetValue(member.MemberSlot, out var memberState);
                bool shouldBeAlive = foundState && memberState.alive;
                if (!shouldBeAlive)
                {
                    if (member.gameObject.activeSelf)
                        member.gameObject.SetActive(false);
                    continue;
                }

                var donor = FindAmmoSupplyDonor(member);
                if (donor != null && foundState && memberState.hasAmmoDonationState)
                    donor.ApplySavedDonationState(memberState.ammoDonated);
            }
        }
    }

    private void ApplySavedIndividualActorStates(SaveFileData data)
    {
        if (data == null || data.individuals == null || data.individuals.Count == 0)
            return;

        var actors = FindObjectsByType<MNRSaveIndividualActor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var lookup = new Dictionary<string, MNRSaveIndividualActor>(StringComparer.Ordinal);

        for (int i = 0; i < actors.Length; i++)
        {
            var actor = actors[i];
            if (actor == null || string.IsNullOrWhiteSpace(actor.SaveId))
                continue;

            if (!lookup.ContainsKey(actor.SaveId))
                lookup.Add(actor.SaveId, actor);
            else
                Debug.LogWarning($"[MNRSaveSystem] Duplicate individual saveId '{actor.SaveId}' found during load. First match will be used.", actor);
        }

        for (int i = 0; i < data.individuals.Count; i++)
        {
            var state = data.individuals[i];
            if (state == null || string.IsNullOrWhiteSpace(state.id))
                continue;

            if (!lookup.TryGetValue(state.id, out var actor) || actor == null)
                continue;

            ApplySavedIndividualState(actor, state);
        }
    }

    private void ApplySavedIndividualState(MNRSaveIndividualActor actor, IndividualActorState state)
    {
        if (actor == null || state == null)
            return;

        if (state.hasLifeState)
        {
            if (!state.alive)
            {
                actor.gameObject.SetActive(false);
                return;
            }

            if (!actor.gameObject.activeSelf)
                actor.gameObject.SetActive(true);
        }

        if (state.hasTransform)
            ApplyTransformState(actor.transform, state.position, state.rotation);

        var activationGate = actor.GetComponent<AllyActivationGate>();
        if (activationGate != null && state.hasActivationState && !state.activationActive)
            activationGate.Deactivate();

        var prisonerState = actor.GetComponent<AllyPrisonerState>();
        if (prisonerState != null && state.hasPrisonerState)
        {
            if (state.prisoner)
                prisonerState.EnterPrisonerMode();
            else
                prisonerState.ReleasePrisonerMode();
        }

        if (activationGate != null && state.hasActivationState && state.activationActive)
            activationGate.Activate();

        var enemyWaypointPatrol = actor.GetComponent<EnemyWaypointPatrol>();
        if (enemyWaypointPatrol != null && state.hasEnemyWaypointPatrol)
            enemyWaypointPatrol.SetPatrolEnabled(state.enemyWaypointPatrolEnabled);

        var allyPingPongPatrol = actor.GetComponent<AllyPatrolPingPong>();
        if (allyPingPongPatrol != null && state.hasAllyPingPongPatrol)
            allyPingPongPatrol.SetPatrolEnabled(state.allyPingPongPatrolEnabled);

        var donor = FindAmmoSupplyDonor(actor);
        if (donor != null && state.hasAmmoDonationState)
            donor.ApplySavedDonationState(state.ammoDonated);
    }

    private static AllyAmmoSupplyDonor FindAmmoSupplyDonor(Component component)
    {
        if (component == null)
            return null;

        return component.GetComponent<AllyAmmoSupplyDonor>()
               ?? component.GetComponentInChildren<AllyAmmoSupplyDonor>(true);
    }

    private static void ApplyTransformState(Transform target, Vector3 position, Quaternion rotation)
    {
        if (target == null)
            return;

        var agent = target.GetComponent<NavMeshAgent>();
        bool warped = false;

        if (agent != null && agent.enabled)
        {
            try
            {
                if (agent.isOnNavMesh)
                {
                    agent.Warp(position);
                    warped = true;
                }
            }
            catch
            {
                // fall back to direct assignment below
            }
        }

        if (!warped)
            target.position = position;

        target.rotation = rotation;
    }

    private static bool HasAnyAliveMembers(TeamState team)
    {
        if (team == null || team.members == null)
            return false;

        for (int i = 0; i < team.members.Count; i++)
        {
            var member = team.members[i];
            if (member != null && member.alive)
                return true;
        }

        return false;
    }

    private static MNRSaveTeamRuntime FindRuntimeBySaveId(string saveId)
    {
        if (string.IsNullOrWhiteSpace(saveId))
            return null;

        var runtimes = FindObjectsByType<MNRSaveTeamRuntime>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < runtimes.Length; i++)
        {
            var runtime = runtimes[i];
            if (runtime == null) continue;
            if (string.Equals(runtime.SaveId, saveId, StringComparison.Ordinal))
                return runtime;
        }

        return null;
    }

    private static bool TrySpawnFromLevelOne(string saveId)
    {
        var all = FindObjectsByType<LevelOne>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            var level = all[i];
            if (level == null) continue;

            if (level.SpawnTeamBySaveId(saveId) != null)
                return true;

            if (level.SpawnAllyTeamBySaveId(saveId) != null)
                return true;
        }

        return false;
    }

    private static bool TrySpawnFromEncounterDirector(string saveId)
    {
        var all = FindObjectsByType<EncounterDirectorPOC>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            var director = all[i];
            if (director == null) continue;

            if (director.SpawnEnemyGroupBySaveId(saveId) != null)
                return true;

            if (director.SpawnAllyGroupBySaveId(saveId) != null)
                return true;
        }

        return false;
    }

    private static SaveFileData ReadSaveFile()
    {
        if (!File.Exists(SaveFilePath))
            return null;

        try
        {
            string json = File.ReadAllText(SaveFilePath);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            return JsonUtility.FromJson<SaveFileData>(json);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MNRSaveSystem] Failed to read save file: {ex.Message}");
            return null;
        }
    }
}
