using UnityEngine;

/// <summary>
/// Keeps the visual "Graphics" child anchored to its starting local position/rotation.
/// This prevents animation clip root/bone offsets from causing visible pops/snaps
/// (like the backwards pop when transitioning Idle -> Run).
///
/// Add this to your Ally's Graphics child (the one with the SkinnedMeshRenderer/Animator).
/// </summary>
public class GraphicsAnchor : MonoBehaviour
{
	private Vector3 startLocalPos;
	private Quaternion startLocalRot;

	private void Awake()
	{
		startLocalPos = transform.localPosition;
		startLocalRot = transform.localRotation;
	}

	private void LateUpdate()
	{
		// Force visuals to stay anchored to the moving parent root.
		transform.localPosition = startLocalPos;
		transform.localRotation = startLocalRot;
	}
}
