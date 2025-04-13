using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PieceCapturedEvent : Unity.Services.Analytics.Event
{
    public PieceCapturedEvent() : base("PieceCaptured")
    {
    }

    public string PieceType
    {
        set => SetParameter("PieceType", value);
    }
    
    public string PieceOwner
    {
        set => SetParameter("PieceOwner", value);
    }
    
    public string CaptureSquare
    {
        set => SetParameter("CaptureSquare", value);
    }
    
    public string FromSquare
    {
        set => SetParameter("FromSquare", value);
    }
    
    public string GameCode
    {
        set => SetParameter("GameCode", value);
    }
}