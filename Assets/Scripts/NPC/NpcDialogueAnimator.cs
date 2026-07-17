using UnityEngine;

/// <summary>
/// NPC konuşma animasyonunu diyalog yöneticisindeki (DialogueManager) durumlara göre otomatik kontrol eder.
/// </summary>
public class NpcDialogueAnimator : MonoBehaviour
{
    [Tooltip("Diyalog veritabanındaki (DialogueNode) konuşmacı (speaker) adı ile eşleşmesi gereken isim.")]
    [SerializeField] private string npcName = "ismail";
    [SerializeField] private Animator animator;

    private void Start()
    {
        if (animator == null)
            animator = GetComponent<Animator>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        if (animator == null)
            animator = GetComponentInParent<Animator>();

        if (DialogueManager.Instance != null)
        {
            DialogueManager.Instance.OnDialogueStateChanged += UpdateTalkingState;
            DialogueManager.Instance.OnDialogueEnded += OnDialogueEnded;
        }
        else
        {
            Debug.LogWarning($"[NpcDialogueAnimator] DialogueManager.Instance bulunamadı. '{gameObject.name}' diyalog durumlarını izleyemeyecek.");
        }
    }

    private void OnDestroy()
    {
        if (DialogueManager.Instance != null)
        {
            DialogueManager.Instance.OnDialogueStateChanged -= UpdateTalkingState;
            DialogueManager.Instance.OnDialogueEnded -= OnDialogueEnded;
        }
    }

    private void UpdateTalkingState()
    {
        if (animator == null || DialogueManager.Instance == null) return;

        if (!DialogueManager.Instance.IsActive)
        {
            SetTalking(false);
            return;
        }

        var currentNode = DialogueManager.Instance.GetCurrentNode();
        if (currentNode != null && !string.IsNullOrEmpty(currentNode.speaker))
        {
            bool isCurrentSpeaker = IsSpeakerMatch(currentNode.speaker, npcName);
            SetTalking(isCurrentSpeaker);
        }
        else
        {
            SetTalking(false);
        }
    }

    private bool IsSpeakerMatch(string speaker, string name)
    {
        if (string.IsNullOrEmpty(speaker) || string.IsNullOrEmpty(name)) return false;

        string s1 = speaker.Replace('İ', 'i').Replace('I', 'ı').ToLowerInvariant().Trim();
        string s2 = name.Replace('İ', 'i').Replace('I', 'ı').ToLowerInvariant().Trim();
        return s1 == s2;
    }

    private void OnDialogueEnded(string sequenceId)
    {
        SetTalking(false);
    }

    private void SetTalking(bool talking)
    {
        if (animator != null && animator.isActiveAndEnabled)
        {
            animator.SetBool("IsTalking", talking);
        }
    }
}
