using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Struct to serialize Move data for network transmission
/// </summary>
public struct SerialisedMove : INetworkSerializable
{
    public SerializedSquare StartSquare;
    public SerializedSquare EndSquare;
    public SerializedSquare SpecialSquare;
    public bool IsSpecialMove;
    public byte SpecialMoveType; // 0=None, 1=Castling, 2=EnPassant, 3=Promotion
    public byte PromotionPieceType; // 0=None, or PieceType value
    
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref StartSquare);
        serializer.SerializeValue(ref EndSquare);
        serializer.SerializeValue(ref SpecialSquare);
        serializer.SerializeValue(ref IsSpecialMove);
        serializer.SerializeValue(ref SpecialMoveType);
        serializer.SerializeValue(ref PromotionPieceType);
    }
}
