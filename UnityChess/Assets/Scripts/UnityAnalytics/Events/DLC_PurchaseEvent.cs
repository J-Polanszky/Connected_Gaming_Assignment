using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DLC_PurchaseEvent : Unity.Services.Analytics.Event
{
    public DLC_PurchaseEvent() : base("DLC_Purchase")
    {
    }

    public string UserID
    {
        set => SetParameter("UserID", value);
    }
    
    public string DLCID
    {
        set => SetParameter("DLCID", value);
    }
    
    public int Price
    {
        set => SetParameter("Price", value);
    }
}