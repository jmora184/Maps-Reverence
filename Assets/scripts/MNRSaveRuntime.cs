using System;
using System.Reflection;
using UnityEngine;

[DisallowMultipleComponent]
public class MNRSaveTeamRuntime : MonoBehaviour
{
    [SerializeField] private string saveId = "";
    [SerializeField] private bool isAlly = false;

    public string SaveId => saveId;
    public bool IsAlly => isAlly;

    public void Configure(string id, bool ally)
    {
        saveId = string.IsNullOrWhiteSpace(id) ? "" : id.Trim();
        isAlly = ally;
    }
}

[DisallowMultipleComponent]
public class MNRSaveMemberRuntime : MonoBehaviour
{
    [SerializeField] private string teamSaveId = "";
    [SerializeField] private int memberSlot = -1;

    public string TeamSaveId => teamSaveId;
    public int MemberSlot => memberSlot;

    public void Configure(string saveId, int slot)
    {
        teamSaveId = string.IsNullOrWhiteSpace(saveId) ? "" : saveId.Trim();
        memberSlot = Mathf.Max(0, slot);
    }

    public bool IsAliveForSave()
    {
        return MNRSaveHealthUtility.IsAlive(gameObject);
    }
}


public static class MNRSaveRuntimeUtility
{
    public static void AttachTeamRuntime(Transform teamRoot, string saveId, bool isAlly)
    {
        if (teamRoot == null || string.IsNullOrWhiteSpace(saveId))
            return;

        var runtime = teamRoot.GetComponent<MNRSaveTeamRuntime>();
        if (runtime == null)
            runtime = teamRoot.gameObject.AddComponent<MNRSaveTeamRuntime>();

        runtime.Configure(saveId, isAlly);
    }

    public static void AttachMemberRuntime(GameObject member, string teamSaveId, int slot)
    {
        if (member == null || string.IsNullOrWhiteSpace(teamSaveId))
            return;

        var runtime = member.GetComponent<MNRSaveMemberRuntime>();
        if (runtime == null)
            runtime = member.gameObject.AddComponent<MNRSaveMemberRuntime>();

        runtime.Configure(teamSaveId, slot);
    }
}

public static class MNRSaveHealthUtility
{
    public static bool IsAlive(GameObject target)
    {
        if (target == null)
            return false;

        if (!target.activeInHierarchy)
            return false;

        var ally = target.GetComponent<AllyHealth>();
        if (ally != null)
            return !ally.IsDead;

        var enemy = target.GetComponent<EnemyHealthController>();
        if (enemy != null)
            return !enemy.IsDead;

        var drone = target.GetComponent<DroneEnemyController>();
        if (drone != null)
        {
            if (ReadBoolMember(drone, "IsDead", false))
                return false;

            if (ReadBoolField(drone, "_isDead"))
                return false;

            float currentHealth = ReadFloatMember(drone, "currentHealth", float.NaN);
            if (!float.IsNaN(currentHealth))
                return currentHealth > 0f;

            return true;
        }

        if (TryReadGenericDeadFlag(target, out bool genericAlive))
            return genericAlive;

        return true;
    }

    private static bool TryReadGenericDeadFlag(GameObject target, out bool alive)
    {
        alive = true;

        var components = target.GetComponents<MonoBehaviour>();
        for (int i = 0; i < components.Length; i++)
        {
            var c = components[i];
            if (c == null) continue;

            if (TryReadNamedBool(c, "IsDead", out bool isDead))
            {
                alive = !isDead;
                return true;
            }

            if (TryReadNamedBool(c, "isDead", out isDead))
            {
                alive = !isDead;
                return true;
            }

            if (TryReadNamedBool(c, "_isDead", out isDead))
            {
                alive = !isDead;
                return true;
            }

            if (TryReadNamedBool(c, "dead", out isDead))
            {
                alive = !isDead;
                return true;
            }
        }

        return false;
    }

    private static bool TryReadNamedBool(object target, string memberName, out bool value)
    {
        value = false;

        if (target == null || string.IsNullOrWhiteSpace(memberName))
            return false;

        try
        {
            var type = target.GetType();
            var prop = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null && prop.PropertyType == typeof(bool) && prop.CanRead)
            {
                value = (bool)prop.GetValue(target);
                return true;
            }

            var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(bool))
            {
                value = (bool)field.GetValue(target);
                return true;
            }
        }
        catch
        {
            // ignore reflection failures
        }

        return false;
    }

    private static bool ReadBoolField(object target, string fieldName)
    {
        if (target == null || string.IsNullOrWhiteSpace(fieldName))
            return false;

        try
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null && field.FieldType == typeof(bool))
                return (bool)field.GetValue(target);
        }
        catch
        {
            // ignore
        }

        return false;
    }

    private static bool ReadBoolMember(object target, string memberName, bool fallback)
    {
        if (target == null || string.IsNullOrWhiteSpace(memberName))
            return fallback;

        try
        {
            var prop = target.GetType().GetProperty(memberName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (prop != null && prop.PropertyType == typeof(bool) && prop.CanRead)
                return (bool)prop.GetValue(target);

            var field = target.GetType().GetField(memberName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null && field.FieldType == typeof(bool))
                return (bool)field.GetValue(target);
        }
        catch
        {
            // ignore
        }

        return fallback;
    }

    private static float ReadFloatMember(object target, string memberName, float fallback)
    {
        if (target == null || string.IsNullOrWhiteSpace(memberName))
            return fallback;

        try
        {
            var prop = target.GetType().GetProperty(memberName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (prop != null && (prop.PropertyType == typeof(float) || prop.PropertyType == typeof(int)) && prop.CanRead)
                return Convert.ToSingle(prop.GetValue(target));

            var field = target.GetType().GetField(memberName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null && (field.FieldType == typeof(float) || field.FieldType == typeof(int)))
                return Convert.ToSingle(field.GetValue(target));
        }
        catch
        {
            // ignore
        }

        return fallback;
    }
}
