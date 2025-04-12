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
        
        print(pieceCapturedEvent.ToString());
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
        
        // AnalyticsService.Instance.RecordEvent(victoryEvent);
    }
}
