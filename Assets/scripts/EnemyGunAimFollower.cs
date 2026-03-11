using UnityEngine;

/// <summary>
/// Rotates AND optionally repositions a weapon transform so it aims toward the current target.
/// Configured here to run while the animator is in Enemy_Idle by default.
/// </summary>
public class EnemyGunAimFollower : MonoBehaviour
{
	[Header("References")]
	[Tooltip("The weapon object to move/rotate, e.g. SM_Weapons_01")]
	public Transform weaponRoot;

	[Tooltip("Existing fire point used by the enemy for shooting")]
	public Transform firePoint;

	[Tooltip("Enemy controller used to discover current target")]
	public Enemy2Controller enemy;

	[Header("Behavior")]
	[Tooltip("If true, only update while the animator is in the chosen state")]
	public bool onlyWhileShooting = true;

	[Tooltip("How fast the gun rotates toward its target")]
	public float rotationSpeed = 18f;

	[Tooltip("How fast the gun moves toward its target local position")]
	public float positionSpeed = 18f;

	[Header("Rotation")]
	[Tooltip("Optional local rotation offset to fix imported weapon axis issues")]
	public Vector3 localEulerOffset = Vector3.zero;

	[Header("Position")]
	[Tooltip("If true, the script can also move the gun locally")]
	public bool updateLocalPosition = true;

	[Tooltip("Base local position to hold the gun at")]
	public Vector3 localPositionOffset = Vector3.zero;

	[Tooltip("Extra local position offset applied only while aiming")]
	public Vector3 aimingLocalPositionOffset = Vector3.zero;

	[Tooltip("If false, the script will leave local position alone")]
	public bool preserveInitialLocalPosition = true;

	[Header("Target Fallback")]
	[Tooltip("Used if no specific aim point is found")]
	public float fallbackAimHeight = 1.2f;

	[Tooltip("Name of child transform on targets to aim at first")]
	public string aimPointChildName = "AimPoint";

	[Header("Animator Gate")]
	[Tooltip("Optional animator reference")]
	public Animator animatorRef;

	[Tooltip("Animator state name that should allow gun rotation/reposition")]
	public string allowedStateName = "Enemy_Idle";

	[Header("Debug")]
	public bool debugDraw = false;

	private Vector3 _initialLocalPos;
	private Quaternion _initialLocalRot;

	private void Reset()
	{
		if (enemy == null) enemy = GetComponent<Enemy2Controller>();
		if (animatorRef == null) animatorRef = GetComponentInChildren<Animator>();
	}

	private void Awake()
	{
		if (enemy == null) enemy = GetComponent<Enemy2Controller>();
		if (animatorRef == null) animatorRef = GetComponentInChildren<Animator>();

		if (weaponRoot != null)
		{
			_initialLocalPos = weaponRoot.localPosition;
			_initialLocalRot = weaponRoot.localRotation;
		}
	}

	private void LateUpdate()
	{
		if (weaponRoot == null || firePoint == null)
			return;

		bool allowed = !onlyWhileShooting || IsLikelyShootingNow();

		if (!allowed)
		{
			RestoreBaseLocalPosition();
			return;
		}

		Transform target = GetCurrentTarget();
		if (target == null)
		{
			RestoreBaseLocalPosition();
			return;
		}

		UpdateWeaponPosition();
		UpdateWeaponRotation(target);
	}

	private void RestoreBaseLocalPosition()
	{
		if (!updateLocalPosition || weaponRoot == null)
			return;

		Vector3 targetLocalPos = preserveInitialLocalPosition
			? (_initialLocalPos + localPositionOffset)
			: localPositionOffset;

		weaponRoot.localPosition = Vector3.Lerp(
			weaponRoot.localPosition,
			targetLocalPos,
			Mathf.Max(0.01f, positionSpeed) * Time.deltaTime
		);
	}

	private void UpdateWeaponPosition()
	{
		if (!updateLocalPosition || weaponRoot == null)
			return;

		Vector3 targetLocalPos = preserveInitialLocalPosition
			? (_initialLocalPos + localPositionOffset + aimingLocalPositionOffset)
			: (localPositionOffset + aimingLocalPositionOffset);

		weaponRoot.localPosition = Vector3.Lerp(
			weaponRoot.localPosition,
			targetLocalPos,
			Mathf.Max(0.01f, positionSpeed) * Time.deltaTime
		);
	}

	private void UpdateWeaponRotation(Transform target)
	{
		if (weaponRoot == null)
			return;

		Vector3 aimPos = GetAimPosition(target);
		Vector3 dir = aimPos - weaponRoot.position;

		if (dir.sqrMagnitude < 0.0001f)
			return;

		Quaternion wantedWorldRot = Quaternion.LookRotation(dir.normalized, Vector3.up);
		wantedWorldRot *= Quaternion.Euler(localEulerOffset);

		weaponRoot.rotation = Quaternion.Slerp(
			weaponRoot.rotation,
			wantedWorldRot,
			Mathf.Max(0.01f, rotationSpeed) * Time.deltaTime
		);

		if (debugDraw)
		{
			Debug.DrawLine(weaponRoot.position, aimPos, Color.red);
		}
	}

	private Transform GetCurrentTarget()
	{
		if (enemy == null)
			return null;

		Transform best = FindNearestLikelyAllyTarget();
		if (best != null)
			return best;

		GameObject player = GameObject.FindGameObjectWithTag("Player");
		if (player != null)
			return player.transform;

		return null;
	}

	private Transform FindNearestLikelyAllyTarget()
	{
		Collider[] hits = Physics.OverlapSphere(transform.position, 20f);
		Transform best = null;
		float bestScore = float.MaxValue;

		for (int i = 0; i < hits.Length; i++)
		{
			if (hits[i] == null) continue;

			AllyController ally = hits[i].GetComponentInParent<AllyController>();
			if (ally == null) continue;
			if (!ally.gameObject.activeInHierarchy) continue;

			Vector3 to = ally.transform.position - transform.position;
			float dist = to.magnitude;
			if (dist <= 0.001f) continue;

			float angle = Vector3.Angle(transform.forward, to.normalized);
			float score = dist + (angle * 0.1f);

			if (score < bestScore)
			{
				bestScore = score;
				best = ally.transform;
			}
		}

		return best;
	}

	private Vector3 GetAimPosition(Transform target)
	{
		if (target == null)
			return transform.position + transform.forward * 10f;

		if (!string.IsNullOrWhiteSpace(aimPointChildName))
		{
			Transform ap = FindDeepChild(target, aimPointChildName);
			if (ap != null)
				return ap.position;
		}

		Collider c = target.GetComponentInChildren<Collider>();
		if (c != null)
			return c.bounds.center;

		return target.position + Vector3.up * fallbackAimHeight;
	}

	private bool IsLikelyShootingNow()
	{
		if (animatorRef == null)
			return true;

		if (HasBoolParam(animatorRef, "fireShot"))
		{
			if (animatorRef.GetBool("fireShot"))
				return true;
		}

		AnimatorStateInfo state = animatorRef.GetCurrentAnimatorStateInfo(0);

		if (!string.IsNullOrWhiteSpace(allowedStateName) && state.IsName(allowedStateName))
			return true;

		return false;
	}

	private static bool HasBoolParam(Animator animator, string paramName)
	{
		if (animator == null || string.IsNullOrWhiteSpace(paramName))
			return false;

		var ps = animator.parameters;
		for (int i = 0; i < ps.Length; i++)
		{
			if (ps[i].name == paramName && ps[i].type == AnimatorControllerParameterType.Bool)
				return true;
		}

		return false;
	}

	private static Transform FindDeepChild(Transform parent, string childName)
	{
		if (parent == null || string.IsNullOrWhiteSpace(childName))
			return null;

		for (int i = 0; i < parent.childCount; i++)
		{
			Transform child = parent.GetChild(i);

			if (child.name == childName)
				return child;

			Transform found = FindDeepChild(child, childName);
			if (found != null)
				return found;
		}

		return null;
	}

	private void OnDrawGizmosSelected()
	{
		if (weaponRoot == null)
			return;

		Gizmos.color = Color.red;
		Gizmos.DrawLine(weaponRoot.position, weaponRoot.position + weaponRoot.forward * 2f);

		if (firePoint != null)
		{
			Gizmos.color = Color.yellow;
			Gizmos.DrawLine(firePoint.position, firePoint.position + firePoint.forward * 2f);
		}
	}
}