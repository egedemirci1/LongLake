using Unity.Netcode;
using UnityEngine;

/// <summary>
/// E → synced DialogueManager. Co-op'ta tüm oyuncular yakındayken başlar; solo'da tek kişi yeter.
/// Başlatma NpcWalkToPoint üzerinden server'da doğrulanır.
/// </summary>
public class NpcDialogueTalk : MonoBehaviour, IInteractable
{
    [SerializeField] private string sequenceId = "ismail_arrival";
    [SerializeField] private string interactPrompt = "Konuş";
    [SerializeField] private string waitingForPartnerPrompt = "Diğer oyuncu da yakında olmalı";
    [SerializeField] private bool interactEnabled;

    [Header("Co-op Proximity")]
    [SerializeField] private bool requireAllPlayersNearby = true;
    [SerializeField] private int minimumPlayersRequired = 2;
    [SerializeField] private float nearbyRadius = 5f;

    private NpcWalkToPoint _startGate;

    public void Configure(string id, string prompt, NpcWalkToPoint startGate = null)
    {
        if (!string.IsNullOrEmpty(id))
            sequenceId = id;
        if (!string.IsNullOrEmpty(prompt))
            interactPrompt = prompt;
        _startGate = startGate;
    }

    public void SetInteractEnabled(bool enabled)
    {
        interactEnabled = enabled;
    }

    public void Interact(InventoryManager interactorInventory)
    {
        if (!interactEnabled) return;
        if (DialogueManager.IsDialogueOpen) return;
        if (!AreAllPlayersNearby()) return;

        if (_startGate != null)
        {
            _startGate.RequestStartArrivalDialogue();
            return;
        }

        if (DialogueManager.Instance == null)
        {
            Debug.LogWarning("[NpcDialogueTalk] DialogueManager missing.");
            return;
        }

        DialogueManager.Instance.TryStartDialogue(sequenceId);
    }

    public string GetInteractText()
    {
        if (!interactEnabled || DialogueManager.IsDialogueOpen)
            return string.Empty;

        if (!AreAllPlayersNearby())
            return waitingForPartnerPrompt;

        return interactPrompt;
    }

    private bool AreAllPlayersNearby()
    {
        if (!requireAllPlayersNearby)
            return true;

        return PlayersNearbyUtility.AreEnoughPlayersNear(
            transform.position,
            nearbyRadius,
            GetMinimumPlayersRequired());
    }

    private int GetMinimumPlayersRequired()
    {
        int connected = 1;
        if (NetworkManager.Singleton != null)
            connected = Mathf.Max(1, NetworkManager.Singleton.ConnectedClientsIds.Count);

        return Mathf.Clamp(connected, 1, Mathf.Max(1, minimumPlayersRequired));
    }
}

/// <summary>Kapı / NPC konuşma yakınlık kontrolü (client + server).</summary>
public static class PlayersNearbyUtility
{
    public static bool AreEnoughPlayersNear(Vector3 origin, float radius, int minimumPlayers)
    {
        int playerCount = 0;
        origin.y = 0f;

        foreach (var pc in Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!pc.IsSpawned) continue;

            Vector3 pos = pc.transform.position;
            pos.y = 0f;
            if (Vector3.Distance(origin, pos) > radius)
                return false;

            playerCount++;
        }

        return playerCount >= minimumPlayers;
    }
}
