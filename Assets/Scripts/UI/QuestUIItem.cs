using UnityEngine;
using TMPro;

public class QuestUIItem : MonoBehaviour
{
    [Header("UI Elements")]
    public TextMeshProUGUI questTitleText;
    public TextMeshProUGUI questDescriptionText;
    
    public void SetupQuest(QuestData quest)
    {
        if (quest == null) return;
        
        if (questTitleText != null)
            questTitleText.text = quest.questTitle;
        
        if (questDescriptionText != null)
            questDescriptionText.text = quest.questDescription;
    }
}
