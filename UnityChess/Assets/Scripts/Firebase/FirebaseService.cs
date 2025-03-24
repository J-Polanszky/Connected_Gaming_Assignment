using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase;
using Firebase.Database;
using Firebase.Extensions;
using UnityEngine;

public class FirebaseService : MonoBehaviourSingleton<FirebaseService>
{
    private FirebaseDatabase _database;

    private void Awake()
    {
        if (Instance != this)
        {
            Destroy(gameObject);
        }

        DontDestroyOnLoad(gameObject);
        
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            if (task.Result == DependencyStatus.Available)
            {
                FirebaseApp app = FirebaseApp.DefaultInstance;
                _database = FirebaseDatabase.GetInstance(app, "https://cg-mcast-default-rtdb.europe-west1.firebasedatabase.app/");
            }
            else
            {
                Debug.LogError("Could not resolve all Firebase dependencies: " + task.Result);
            }
        });
    }

    public void SaveGame(string sessionCode, string serialisedGame)
    {
        DatabaseReference gameRef = _database.GetReference(sessionCode);
        gameRef.SetValueAsync(serialisedGame).ContinueWith(task =>
        {
            if (task.IsFaulted)
                Debug.LogError("Failed to save game: " + task.Exception);
            else if (task.IsCompleted)
                Debug.Log("Game saved successfully");
        });
    }
    
    public async Task<string> LoadGame(string sessionCode)
    {
        DatabaseReference gameRef = _database.GetReference(sessionCode);
        DataSnapshot snapshot = await gameRef.GetValueAsync();
        try
        {
            return snapshot.Value.ToString();
        }catch (NullReferenceException e)
        {
            Debug.LogError("Failed to load game: " + e);
            return null;
        }
    }
}