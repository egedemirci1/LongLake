using System;
using UnityEngine;

[Serializable]
public class DialogueChoice
{
    public string label;
    [Tooltip("Empty = end dialogue after this choice.")]
    public string nextNodeId;
}

[Serializable]
public class DialogueNode
{
    public string id;
    public string speaker;
    [TextArea(2, 6)]
    public string text;
    public AudioClip voiceClip;
    [Tooltip("Empty = Continue-only line.")]
    public DialogueChoice[] choices = Array.Empty<DialogueChoice>();
    [Tooltip("Used when choices is empty. Empty nextNodeId ends the dialogue.")]
    public string nextNodeId;
}

[CreateAssetMenu(fileName = "New Dialogue", menuName = "Dialogue System/Dialogue Sequence")]
public class DialogueSequence : ScriptableObject
{
    public string sequenceId;
    public string startNodeId;
    public DialogueNode[] nodes = Array.Empty<DialogueNode>();
    [Tooltip("Optional quest id to complete when this sequence ends normally.")]
    public string completeQuestId;
    [Tooltip("Optional quest id to start when this sequence ends normally.")]
    public string startQuestId;
}
