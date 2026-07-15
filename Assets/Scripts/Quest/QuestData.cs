using UnityEngine;

[CreateAssetMenu(fileName = "New Quest", menuName = "Quest System/Quest Data")]
public class QuestData : ScriptableObject
{
    [Header("Quest Information")]
    public string questID;
    public string questTitle;
    [TextArea(3, 5)]
    public string questDescription;
    
    [Header("Quest Status")]
    public QuestStatus initialStatus = QuestStatus.NotStarted;
}

public enum QuestStatus
{
    NotStarted,
    Active,
    Completed
}
