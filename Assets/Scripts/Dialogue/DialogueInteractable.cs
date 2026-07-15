using Unity.Netcode;
using UnityEngine;

/// <summary>
/// IInteractable that starts a synced DialogueManager sequence.
/// </summary>
public class DialogueInteractable : NetworkBehaviour, IInteractable
{
    [SerializeField] private string sequenceId = "sample_intro";
    [SerializeField] private string requiredQuestId;
    [SerializeField] private string interactPrompt = "Konuş";

    public void Interact(InventoryManager interactorInventory)
    {
        if (DialogueManager.IsDialogueOpen) return;

        if (!string.IsNullOrEmpty(requiredQuestId))
        {
            if (QuestManager.Instance == null || !QuestManager.Instance.IsCurrentQuestId(requiredQuestId))
                return;
        }

        if (DialogueManager.Instance == null)
        {
            Debug.LogWarning("[DialogueInteractable] DialogueManager missing.");
            return;
        }

        DialogueManager.Instance.TryStartDialogue(sequenceId);
    }

    public string GetInteractText()
    {
        if (DialogueManager.IsDialogueOpen)
            return "";

        if (!string.IsNullOrEmpty(requiredQuestId))
        {
            if (QuestManager.Instance == null || !QuestManager.Instance.IsCurrentQuestId(requiredQuestId))
                return "";
        }

        return interactPrompt;
    }
}
