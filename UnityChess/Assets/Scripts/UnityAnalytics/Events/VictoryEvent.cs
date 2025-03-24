using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VictoryEvent : Unity.Services.Analytics.Event
{
    public VictoryEvent() : base("Victory")
    {
    }

    public string Winner
    {
        set => SetParameter("Winner", value);
    }
    
    public string VictoryType
    {
        set => SetParameter("VictoryType", value);
    }
    
    public string GameCode
    {
        set => SetParameter("GameCode", value);
    }
}