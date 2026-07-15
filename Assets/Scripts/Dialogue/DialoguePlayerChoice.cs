using System;
using Unity.Netcode;

/// <summary>Per-participant choice row for a choice node (NetworkList element).</summary>
public struct DialoguePlayerChoice : INetworkSerializable, IEquatable<DialoguePlayerChoice>
{
    public ulong ClientId;
    public int ChoiceIndex;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref ChoiceIndex);
    }

    public bool Equals(DialoguePlayerChoice other)
    {
        return ClientId == other.ClientId && ChoiceIndex == other.ChoiceIndex;
    }

    public override bool Equals(object obj) => obj is DialoguePlayerChoice other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(ClientId, ChoiceIndex);
}
