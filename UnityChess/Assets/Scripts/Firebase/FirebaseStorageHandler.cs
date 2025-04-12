using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Firebase.Storage;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public class AvatarManifest
{
    public AvatarData[] avatars;
}

[Serializable]
public class AvatarData
{
    public string id;
    public string path;
    public string fileType;
    public float price;
}

public class FirebaseStorageHandler : MonoBehaviourSingleton<FirebaseStorageHandler>
{
    [SerializeField] private GameObject shopCanvas;
    [SerializeField] private Transform avatarItemsContainer;
    // [SerializeField] private GameObject loadingIndicator;
    // [SerializeField] private Button shopButton;
    // [SerializeField] private Button closeShopButton;

    private FirebaseStorage _storage;
    private StorageReference _storageRef;

    private string _localPath;
    private bool _shopInitialized = false;
    private bool _isPurchasing = false;

    // Avatar data collections
    private Dictionary<string, AvatarData> _avatarDictionary = new Dictionary<string, AvatarData>();
    private Dictionary<string, Texture2D> _previewCache = new Dictionary<string, Texture2D>();
    private UserAvatarData _userData = new UserAvatarData();
    
    public GameObject avatarPreviewPrefab;
    
    private string _equippedAvatarId = "default";
    public string EquippedAvatarId => _equippedAvatarId;

    public UserAvatarData UserData
    {
        get => _userData;
        set
        {
            _userData = value;
            if (_userData != null)
            {
                _equippedAvatarId = _userData.equippedAvatar;
            }
            else
            {
                _equippedAvatarId = "default";
            }
        }
    }
    
    private void Awake()
    {
        if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        DontDestroyOnLoad(gameObject);

        // Create local directory for storing avatars
        _localPath = Path.Combine(Application.persistentDataPath, "Avatars");
        if (!Directory.Exists(_localPath))
        {
            Directory.CreateDirectory(_localPath);
        }
    }

    private void Start()
    {
        // Initialize Firebase components
        _storage = FirebaseStorage.DefaultInstance;
        _storageRef = _storage.RootReference;
        
        // Setup shop UI if present
        // if (shopButton != null)
        // {
        //     shopButton.onClick.AddListener(OpenShop);
        // }
        //
        // if (closeShopButton != null)
        // {
        //     closeShopButton.onClick.AddListener(CloseShop);
        // }
        
        if (shopCanvas != null)
        {
            shopCanvas.SetActive(false);
        }
        
        // Load avatar previews in the background
        avatarPreviewPrefab = Resources.Load<GameObject>("Shop/AvatarPreview");
    }
    
    /// <summary>
    /// Opens the avatar shop UI
    /// </summary>
    public void OpenShop()
    {
        if (shopCanvas != null)
        {
            shopCanvas.SetActive(true);
            
            if (!_shopInitialized)
            {
                StartCoroutine(InitializeShop());
            }
        }
    }
    
    /// <summary>
    /// Closes the avatar shop UI
    /// </summary>
    public void CloseShop()
    {
        if (shopCanvas != null)
        {
            shopCanvas.SetActive(false);
        }
    }
    
    /// <summary>
    /// Initializes the shop by loading avatars from Firebase
    /// </summary>
    private IEnumerator InitializeShop()
    {
        // if (loadingIndicator != null)
        //     loadingIndicator.SetActive(true);
            
        // Clear any existing items
        foreach (Transform child in avatarItemsContainer)
        {
            Destroy(child.gameObject);
        }
        
        // Load avatar manifest
        Task loadManifestTask = LoadAvatarManifest();
        yield return new WaitUntil(() => loadManifestTask.IsCompleted);
        
        // if (loadingIndicator != null)
        //     loadingIndicator.SetActive(false);
            
        _shopInitialized = true;
    }

    /// <summary>
    /// Loads the avatar manifest from Firebase Storage
    /// </summary>
    private async Task LoadAvatarManifest()
    {
        if (_avatarDictionary.Count > 0)
            return;
        
        try
        {
            byte[] manifestData = await DownloadFileBytes("Avatars/manifest.json", 10 * 1024); // 10KB limit
            ProcessManifest(manifestData);
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to load avatar manifest: {e}");
        }
    }

    /// <summary>
    /// Downloads a file from Firebase Storage as byte array
    /// </summary>
    public async Task<byte[]> DownloadFileBytes(string path, long maxSize)
    {
        try
        {
            StorageReference fileRef = _storageRef.Child(path);
            return await fileRef.GetBytesAsync(maxSize);
        }
        catch (Exception e)
        {
            Debug.LogError($"Error downloading {path}: {e.Message}");
            throw;
        }
    }

    /// <summary>
    /// Processes the avatar manifest JSON and creates preview items
    /// </summary>
    private void ProcessManifest(byte[] data)
    {
        string json = System.Text.Encoding.UTF8.GetString(data);
        AvatarManifest manifest = JsonUtility.FromJson<AvatarManifest>(json);

        if (manifest == null || manifest.avatars == null)
        {
            Debug.LogError("Failed to parse avatar manifest");
            return;
        }

        for (var i = 0; i < manifest.avatars.Length; i++)
        {
            AvatarData avatarData = manifest.avatars[i];
            _avatarDictionary.Add(avatarData.id, avatarData);
            StartCoroutine(LoadPreview(avatarData, i));
        }
    }

    /// <summary>
    /// Loads a preview image for an avatar and creates a UI element
    /// </summary>
    private IEnumerator LoadPreview(AvatarData avatarData, int index)
    {
        string previewPath = $"Avatars/{avatarData.path}_preview{avatarData.fileType}";

        if (!_previewCache.ContainsKey(avatarData.id))
        {
            // Download preview image
            Task<byte[]> downloadTask = DownloadFileBytes(previewPath, 20 * 1024); // 20KB limit
            yield return new WaitUntil(() => downloadTask.IsCompleted);
            
            if (downloadTask.Exception != null)
            {
                Debug.LogError($"Failed to download preview for {avatarData.id}: {downloadTask.Exception}");
                yield break;
            }
            
            // Create texture from downloaded bytes
            Texture2D loadedTexture = new Texture2D(1, 1);
            loadedTexture.LoadImage(downloadTask.Result);
            _previewCache.Add(avatarData.id, loadedTexture);
        }

        // Create preview item in shop UI
        Texture2D texture = _previewCache[avatarData.id];
        GameObject previewObject = Instantiate(avatarPreviewPrefab, avatarItemsContainer);
        
        // Setup the preview image
        Image previewImage = previewObject.GetComponent<Image>();
        previewImage.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), 
            new Vector2(0.5f, 0.5f));
            
        // Setup purchase/equip button
        Button button = previewObject.GetComponent<Button>();
        bool isOwned = _userData.ownedAvatars.ContainsKey(avatarData.id) && _userData.ownedAvatars[avatarData.id];
        bool isEquipped = _userData.equippedAvatar == avatarData.id;
        
        // Setup UI elements based on ownership status
        TextMeshProUGUI buttonText = button.GetComponentInChildren<TextMeshProUGUI>();
        if (isOwned)
        {
            if (isEquipped)
            {
                buttonText.text = "EQUIPPED";
                button.interactable = false;
            }
            else
            {
                buttonText.text = "EQUIP";
                button.onClick.AddListener(() => EquipAvatar(avatarData.id));
            }
        }
        else
        {
            buttonText.text = $"{avatarData.price} COINS";
            button.onClick.AddListener(() => PurchaseAvatar(avatarData.id));
        }
        
        // Position the item in the grid
        RectTransform rectTransform = previewObject.GetComponent<RectTransform>();
        float itemHeight = 200f;
        float itemWidth = 200f;
        int itemsPerRow = 3;
        
        int row = index / itemsPerRow;
        int col = index % itemsPerRow;
        
        rectTransform.anchoredPosition = new Vector2(col * itemWidth, -row * itemHeight);
    }

    /// <summary>
    /// Purchases an avatar using in-game currency
    /// </summary>
    public async Task PurchaseAvatar(string avatarId)
    {
        if (_isPurchasing || !_avatarDictionary.ContainsKey(avatarId))
            return;

        _isPurchasing = true;
        
        try
        {
            AvatarData avatarData = _avatarDictionary[avatarId];
            
            // For now, force purchase to succeed (we'll add currency check later)
            // TODO:
            bool purchaseSuccessful = true;
            
            if (purchaseSuccessful)
            {
                // Download the full avatar
                string avatarPath = $"Avatars/{avatarData.path}{avatarData.fileType}";
                await DownloadAvatar(avatarPath, avatarId);
                
                // Update user data
                _userData.ownedAvatars[avatarId] = true;
                // await SaveUserAvatarData();
                await FirebaseService.Instance.SaveUserAvatarData( _userData);
                
                // Refresh shop UI
                _shopInitialized = false;
                StartCoroutine(InitializeShop());
                
                Debug.Log($"Successfully purchased avatar: {avatarId}");
            }
            else
            {
                Debug.Log("Not enough currency to purchase avatar");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error purchasing avatar: {e.Message}");
        }
        
        _isPurchasing = false;
    }

    /// <summary>
    /// Equips an avatar that the user owns
    /// </summary>
    public async void EquipAvatar(string avatarId)
    {
        if (!_userData.ownedAvatars.ContainsKey(avatarId) || !_userData.ownedAvatars[avatarId])
        {
            Debug.LogWarning($"Cannot equip avatar {avatarId} - not owned");
            return;
        }
        
        _userData.equippedAvatar = avatarId;
        _equippedAvatarId = avatarId;
        
        // await SaveUserAvatarData();
        await FirebaseService.Instance.SaveUserAvatarData(_userData);
        
        // Refresh shop UI
        _shopInitialized = false;
        StartCoroutine(InitializeShop());
        
        // Notify GameManager that avatar changed
        if (GameManager.Instance != null)
        {
            Texture2D avatarTexture = await GetCurrentAvatarTexture();
            GameManager.Instance.SetAvatar(avatarTexture, true, _equippedAvatarId);
        }
        
        Debug.Log($"Equipped avatar: {avatarId}");
    }

    /// <summary>
    /// Downloads an avatar file from Firebase Storage
    /// </summary>
    private async Task DownloadAvatar(string avatarPath, string avatarId)
    {
        try
        {
            string localFilePath = Path.Combine(_localPath, Path.GetFileName(avatarPath));
            
            // Download the avatar if it doesn't exist locally
            if (!File.Exists(localFilePath))
            {
                byte[] avatarData = await DownloadFileBytes(avatarPath, 50 * 1024); // 50KB limit
                await File.WriteAllBytesAsync(localFilePath, avatarData);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error downloading avatar: {e.Message}");
            throw;
        }
    }

    /// <summary>
    /// Gets the texture for the currently equipped avatar
    /// </summary>
    public async Task<Texture2D> GetCurrentAvatarTexture()
    {
        if (string.IsNullOrEmpty(_userData.equippedAvatar) || _userData.equippedAvatar == "default")
        {
            // Return default avatar
            return LoadDefaultAvatar();
        }
        
        if (!_avatarDictionary.ContainsKey(_userData.equippedAvatar))
        {
            Debug.LogWarning($"Avatar {_userData.equippedAvatar} not found in dictionary, using default");
            return LoadDefaultAvatar();
        }
        
        AvatarData avatarData = _avatarDictionary[_userData.equippedAvatar];
        string avatarPath = $"Avatars/{avatarData.path}{avatarData.fileType}";
        string localFilePath = Path.Combine(_localPath, Path.GetFileName(avatarPath));
        
        // Download if not cached locally
        if (!File.Exists(localFilePath))
        {
            await DownloadAvatar(avatarPath, _userData.equippedAvatar);
        }
        
        // Load from local path
        Texture2D texture = new Texture2D(1, 1);
        byte[] fileData = await File.ReadAllBytesAsync(localFilePath);
        texture.LoadImage(fileData);
        
        return texture;
    }

    /// <summary>
    /// Gets the texture for a specific avatar by ID
    /// </summary>
    public async Task<Texture2D> GetAvatarTexture(string avatarId)
    {
        if (string.IsNullOrEmpty(avatarId) || avatarId == "default" || !_avatarDictionary.ContainsKey(avatarId))
        {
            return LoadDefaultAvatar();
        }
        
        AvatarData avatarData = _avatarDictionary[avatarId];
        string avatarPath = $"Avatars/{avatarData.path}{avatarData.fileType}";
        string localFilePath = Path.Combine(_localPath, Path.GetFileName(avatarPath));
        
        // Download if not cached locally
        if (!File.Exists(localFilePath))
        {
            await DownloadAvatar(avatarPath, avatarId);
        }
        
        // Load from local path
        Texture2D texture = new Texture2D(1, 1);
        byte[] fileData = await File.ReadAllBytesAsync(localFilePath);
        texture.LoadImage(fileData);
        
        return texture;
    }

    /// <summary>
    /// Loads the default avatar texture
    /// </summary>
    private Texture2D LoadDefaultAvatar()
    {
        // Load built-in default avatar
        Texture2D defaultAvatar = Resources.Load<Texture2D>("Avatars/DefaultAvatar");
        if (defaultAvatar == null)
        {
            defaultAvatar = new Texture2D(1, 1);
            defaultAvatar.SetPixel(0, 0, Color.white);
            defaultAvatar.Apply();
        }
        return defaultAvatar;
    }

    // /// <summary>
    // /// Loads user avatar data from Firebase Database
    // /// </summary>
    // private async Task LoadUserAvatarData()
    // {
    //     try
    //     {
    //         if (!AuthenticationService.Instance.IsSignedIn)
    //         {
    //             Debug.LogWarning("User not signed in, using default avatar data");
    //             InitializeDefaultUserData();
    //             return;
    //         }
    //         
    //         string userId = AuthenticationService.Instance.PlayerId;
    //         DatabaseReference userRef = _databaseRef.Child("users").Child(userId).Child("avatars");
    //         
    //         DataSnapshot snapshot = await userRef.GetValueAsync();
    //         if (snapshot.Exists)
    //         {
    //             string json = snapshot.GetRawJsonValue();
    //             _userData = JsonUtility.FromJson<UserAvatarData>(json) ?? new UserAvatarData();
    //             
    //             if (_userData.ownedAvatars == null)
    //                 _userData.ownedAvatars = new Dictionary<string, bool>();
    //                 
    //             // Set equipped avatar ID
    //             _equippedAvatarId = _userData.equippedAvatar;
    //         }
    //         else
    //         {
    //             InitializeDefaultUserData();
    //             await SaveUserAvatarData();
    //         }
    //     }
    //     catch (Exception e)
    //     {
    //         Debug.LogError($"Error loading user avatar data: {e.Message}");
    //         InitializeDefaultUserData();
    //     }
    // }
    //
    // /// <summary>
    // /// Initializes default user data for new users
    // /// </summary>
    // private void InitializeDefaultUserData()
    // {
    //     _userData = new UserAvatarData();
    //     _userData.ownedAvatars = new Dictionary<string, bool>();
    //     _userData.ownedAvatars["default"] = true;
    //     _userData.equippedAvatar = "default";
    //     _equippedAvatarId = "default";
    // }
    //
    // /// <summary>
    // /// Saves user avatar data to Firebase Database
    // /// </summary>
    // private async Task SaveUserAvatarData()
    // {
    //     if (!AuthenticationService.Instance.IsSignedIn)
    //     {
    //         Debug.LogWarning("User not signed in, cannot save avatar data");
    //         return;
    //     }
    //     
    //     try
    //     {
    //         string userId = AuthenticationService.Instance.PlayerId;
    //         string json = JsonUtility.ToJson(_userData);
    //         
    //         await _databaseRef.Child("users").Child(userId).Child("avatars").SetRawJsonValueAsync(json);
    //         Debug.Log("User avatar data saved successfully");
    //     }
    //     catch (Exception e)
    //     {
    //         Debug.LogError($"Error saving user avatar data: {e.Message}");
    //     }
    // }
}