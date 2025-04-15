using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameStartedEvent : Unity.Services.Analytics.Event
{
    public GameStartedEvent() : base("GameStartedEvent")
    {
    }

    public string SerialisedGame
    {
        set => SetParameter("SerialisedGame", value);
    }
    
    public string GameCode
    {
        set => SetParameter("GameCode", value);
    }
}
