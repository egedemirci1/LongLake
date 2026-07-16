using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Quest-knock kapısı açılınca İsmail diyaloğunu başlatır.
/// Diyalog bitince NpcWalkToPoint ile arabaya yürüyüşü tetikler.
/// </summary>
public class KnockDoorDialogueTrigger : NetworkBehaviour
{
    [Header("Dialogue")]
    [SerializeField] private string sequenceId = "ismail_knock";

    [Header("After Dialogue")]
    [SerializeField] private NpcWalkToPoint walkOnDialogueEnd;

    private readonly NetworkVariable<bool> dialogueStarted = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private bool _endHooked;

    public override void OnNetworkSpawn()
    {
        ResolveWalk();
        TryHookDialogueEnd();
    }

    public override void OnNetworkDespawn()
    {
        UnhookDialogueEnd();
    }

    private void Update()
    {
        if (!IsServer || dialogueStarted.Value) return;

        TryHookDialogueEnd();

        if (!IsAnyQuestKnockDoorOpen())
            return;

        if (DialogueManager.Instance == null || DialogueManager.Instance.IsActive)
            return;

        dialogueStarted.Value = true;
        DialogueManager.Instance.TryStartDialogue(sequenceId);
        Debug.Log($"<b>[KAPI→DİYALOG]</b> Kapı açıldı, '{sequenceId}' başlıyor.");
    }

    private static bool IsAnyQuestKnockDoorOpen()
    {
        foreach (var door in FindObjectsByType<DoorController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (door.IsQuestKnockDoor && door.IsNavMeshPassageOpen)
                return true;
        }
        return false;
    }

    private void TryHookDialogueEnd()
    {
        if (_endHooked || DialogueManager.Instance == null) return;
        DialogueManager.Instance.OnDialogueEnded += OnDialogueEnded;
        _endHooked = true;
    }

    private void UnhookDialogueEnd()
    {
        if (!_endHooked || DialogueManager.Instance == null) return;
        DialogueManager.Instance.OnDialogueEnded -= OnDialogueEnded;
        _endHooked = false;
    }

    private void OnDialogueEnded(string endedSequenceId)
    {
        if (endedSequenceId != sequenceId) return;

        ResolveWalk();
        if (walkOnDialogueEnd == null)
        {
            Debug.LogWarning("[KnockDoorDialogueTrigger] NpcWalkToPoint yok — yürüyüş başlatılamadı.");
            return;
        }

        walkOnDialogueEnd.StartJourney();
        Debug.Log("<b>[DİYALOG→YÜRÜYÜŞ]</b> Diyalog bitti; İsmail arabaya doğru yürüyor.");
    }

    private void ResolveWalk()
    {
        if (walkOnDialogueEnd != null) return;
        walkOnDialogueEnd = GetComponent<NpcWalkToPoint>();
        if (walkOnDialogueEnd == null)
            walkOnDialogueEnd = FindFirstObjectByType<NpcWalkToPoint>();
    }
}
