using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase;
using Firebase.Auth;
using Firebase.Database;
using Firebase.Extensions;
using UnityEngine;

[Serializable]
public class UserAvatarData
{
    public Dictionary<string, bool> ownedAvatars = new();
    public string equippedAvatar;
    public int currency = 100; // Default starting currency
}

public class FirebaseService : MonoBehaviourSingleton<FirebaseService>
{
    private FirebaseDatabase _database;
    private FirebaseAuth _auth;
    private FirebaseUser _user;

    private string userID = string.Empty;
    
    public string UserID
    {
        get => userID;
        set
        {
            userID = value;
            if (!string.IsNullOrEmpty(userID))
            {
                // Load user data from Firebase
                LoadUserAvatarData();
            }
        }
    }
    
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
                _auth = FirebaseAuth.DefaultInstance;
                
                Debug.Log("Firebase dependencies resolved successfully.");
            }
            else
            {
                Debug.LogError("Could not resolve all Firebase dependencies: " + task.Result);
            }
        });
    }
    
    public async Task SaveUserAvatarData(UserAvatarData avatarData)
    {
        DatabaseReference userRef = _database.GetReference("Users/" + UserID);
        await userRef.SetRawJsonValueAsync(JsonUtility.ToJson(avatarData));
    }
    
    void LoadUserAvatarData()
    {
        DatabaseReference userRef = _database.GetReference("Users/" + UserID);
        userRef.GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted)
            {
                Debug.LogError("Failed to load user data: " + task.Exception);
            }
            else if (task.IsCompleted)
            {
                DataSnapshot snapshot = task.Result;
                if (snapshot.Exists)
                {
                    UserAvatarData userAvatarData = JsonUtility.FromJson<UserAvatarData>(snapshot.GetRawJsonValue());
                    FirebaseStorageHandler.Instance.UserData = userAvatarData;
                    Debug.Log("User data loaded successfully: " + userAvatarData);
                }
                else
                {
                    Debug.Log("No data available for this user.");
                    // Initialize with default data
                    UserAvatarData newUserAvatarData = new UserAvatarData
                    {
                        ownedAvatars = new Dictionary<string, bool>(),
                        equippedAvatar = "default_avatar",
                        currency = 100
                    };
                    FirebaseStorageHandler.Instance.UserData = newUserAvatarData;
                    SaveUserAvatarData(newUserAvatarData).ContinueWith(saveTask =>
                    {
                        if (saveTask.IsFaulted)
                        {
                            Debug.LogError("Failed to save new user data: " + saveTask.Exception);
                        }
                        else
                        {
                            Debug.Log("New user data saved successfully.");
                        }
                    });
                }
            }
        });
    }

    public void SaveGame(string sessionCode, string serialisedGame)
    {
        DatabaseReference gameRef = _database.GetReference("Games/" + sessionCode);
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
        DatabaseReference gameRef = _database.GetReference("Games/" + sessionCode);
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