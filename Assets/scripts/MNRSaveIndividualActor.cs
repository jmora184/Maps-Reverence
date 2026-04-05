using UnityEngine;

[DisallowMultipleComponent]
public class MNRSaveIndividualActor : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField] private string saveId = "";

    [Header("What To Save")]
    [SerializeField] private bool saveTransform = true;
    [SerializeField] private bool saveLifeState = true;
    [SerializeField] private bool saveActivationState = true;
    [SerializeField] private bool savePrisonerState = true;
    [SerializeField] private bool savePatrolState = true;

    public string SaveId => string.IsNullOrWhiteSpace(saveId) ? "" : saveId.Trim();
    public bool SaveTransform => saveTransform;
    public bool SaveLifeState => saveLifeState;
    public bool SaveActivationState => saveActivationState;
    public bool SavePrisonerState => savePrisonerState;
    public bool SavePatrolState => savePatrolState;

    public bool IsAliveForSave()
    {
        return MNRSaveHealthUtility.IsAlive(gameObject);
    }
}
