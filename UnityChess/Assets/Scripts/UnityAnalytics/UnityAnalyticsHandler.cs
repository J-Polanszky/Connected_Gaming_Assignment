using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Services.Analytics;
using UnityChess;
using UnityEngine;

public enum VictoryType
{
    Checkmate,
    Stalemate,
    Resignation,
}

public class UnityAnalyticsHandler : MonoBehaviourSingleton<UnityAnalyticsHandler>
{
    private void Awake()
    {
        if(Instance != this)
        {
            Destroy(gameObject);
        }
        
        DontDestroyOnLoad(gameObject);
        // This is done in the networkmanagerhandler
        // UnityServices.InitializeAsync();
    }

    public void OnServicesInitialised()
    {
        AnalyticsService.Instance.StartDataCollection();
    }

    public void NewGameEvent(string serialisedGame, string gameCode)
    {
        GameStartedEvent newGameEvent = new GameStartedEvent
        {
            GameCode = gameCode,
            SerialisedGame = serialisedGame
        };
        
        AnalyticsService.Instance.RecordEvent(newGameEvent);
    }
    
    public void RecordPieceCaptured(Piece piece, Movement move, string gameCode)
    {
        PieceCapturedEvent pieceCapturedEvent = new PieceCapturedEvent
        {
            PieceType = piece.GetPieceType().ToString(),
            PieceOwner = piece.Owner.ToString(),
            CaptureSquare = move.End.ToString(),
            FromSquare = move.Start.ToString(),
            GameCode = gameCode
        };
        
        Debug.Log(pieceCapturedEvent.ToString());
        // Not sending to analytics as it seems excessive for the task at hand,
        // and would burn through the free quota quickly.
        // AnalyticsService.Instance.RecordEvent(pieceCapturedEvent);
    }
    
    public void RecordVictory(bool didWhiteWin, VictoryType victoryType, string gameCode)
    {
        string winner = didWhiteWin ? "White" : "Black";
        string victory = victoryType.ToString();

        VictoryEvent victoryEvent = new VictoryEvent
        {
            Winner = winner,
            VictoryType = victory,
            GameCode = gameCode
        };
        
        AnalyticsService.Instance.RecordEvent(victoryEvent);
    }

    public void RecordPurchase(string userID, string itemID, int price)
    {
        DLC_PurchaseEvent purchaseEvent = new DLC_PurchaseEvent
        {
            UserID = userID,
            DLCID = itemID,
            Price = price,
        };
        
        AnalyticsService.Instance.RecordEvent(purchaseEvent);
    }
}
