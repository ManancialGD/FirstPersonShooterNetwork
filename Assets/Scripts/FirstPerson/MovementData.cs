using System;
using Unity.Netcode;
using UnityEngine;

[Serializable]
public class MovementData : INetworkSerializable
{
    public int tick;
    public Vector2 input;
    public Vector3 position;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref tick);
        serializer.SerializeValue(ref input);
        serializer.SerializeValue(ref position);
    }
}
