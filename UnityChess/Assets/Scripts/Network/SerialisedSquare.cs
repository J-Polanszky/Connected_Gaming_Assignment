using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Struct to serialize Square for network transmission
/// </summary>
public struct SerializedSquare : INetworkSerializable
{
    public int File;
    public int Rank;
    
    public SerializedSquare(int file, int rank)
    {
        File = file;
        Rank = rank;
    }
    
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref File);
        serializer.SerializeValue(ref Rank);
    }
}