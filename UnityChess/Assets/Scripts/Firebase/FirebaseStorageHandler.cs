using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Firebase.Storage;
using UnityEngine;

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
    public int price;
}

/// <summary>
/// Handles downloading and managing avatar assets from Firebase Storage.
/// This class persists across scenes.
/// </summary>
public class FirebaseStorageHandler : MonoBehaviourSingleton<FirebaseStorageHandler>
{
    private FirebaseStorage _storage;
    private StorageReference _storageRef;

    private string _localPath;
    private bool _manifestLoaded = false;

    // Avatar data collections
    private Dictionary<string, AvatarData> _avatarDictionary = new();
    private Dictionary<string, Texture2D> _previewCache = new(); // Memory-only cache
    private Dictionary<string, Texture2D> _avatarTextureCache = new();
    private UserAvatarData _userData = new();

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

    public Dictionary<string, AvatarData> AvatarDictionary => _avatarDictionary;

    // Event fired when avatar manifest is loaded
    public static event Action OnAvatarManifestLoaded;

    // Event fired when equipped avatar changes
    public static event Action<string> OnAvatarEquipped;

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

    public void OnFirebaseInitialised()
    {
        // Initialize Firebase components
        _storage = FirebaseStorage.DefaultInstance;
        _storageRef = _storage.RootReference;
    }

    private void Start()
    {
        // Initialize Firebase components
        _storage = FirebaseStorage.DefaultInstance;
        _storageRef = _storage.RootReference;

        // Listen for authentication events
        FirebaseService.OnUserDataLoaded += OnUserDataLoaded;
    }

    private void OnDestroy()
    {
        FirebaseService.OnUserDataLoaded -= OnUserDataLoaded;
    }

    private async void OnUserDataLoaded(UserAvatarData userData)
    {
        // Update our local copy
        UserData = userData;

        // Ensure manifest is loaded
        if (!_manifestLoaded)
        {
            await LoadAvatarManifest();
        }

        // Check if equipped avatar is available locally, download if not
        if (!string.IsNullOrEmpty(_equippedAvatarId) && _equippedAvatarId != "default")
        {
            string avatarFilePath = GetLocalAvatarPath(_equippedAvatarId);
            if (!File.Exists(avatarFilePath))
            {
                await EnsureOwnedAvatarIsDownloaded(_equippedAvatarId);
            }
        }
    }

    /// <summary>
    /// Loads the avatar manifest from Firebase Storage
    /// </summary>
    public async Task<bool> LoadAvatarManifest()
    {
        if (_manifestLoaded)
            return true;

        try
        {
            byte[] manifestData = await DownloadFileBytes("Avatars/manifest.json", 10 * 1024); // 10KB limit
            ProcessManifest(manifestData);

            _manifestLoaded = true;

            // Notify listeners that the manifest has been loaded
            OnAvatarManifestLoaded?.Invoke();

            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to load avatar manifest: {e}");
            return false;
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
    /// Processes the avatar manifest JSON
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

        // Clear existing dictionary to prevent duplicates
        _avatarDictionary.Clear();

        // Add default avatar
        if (!_avatarDictionary.ContainsKey("default"))
        {
            AvatarData defaultData = new AvatarData
            {
                id = "default",
                path = "default",
                fileType = ".png",
                price = 0
            };
            _avatarDictionary.Add("default", defaultData);
        }

        for (var i = 0; i < manifest.avatars.Length; i++)
        {
            AvatarData avatarData = manifest.avatars[i];
            _avatarDictionary[avatarData.id] = avatarData;
        }
    }

    /// <summary>
    /// Gets a cached preview or downloads it if not available.
    /// Previews are only kept in memory, never saved to disk.
    /// </summary>
    public async Task<Texture2D> GetAvatarPreview(string avatarId)
    {
        if (!_avatarDictionary.ContainsKey(avatarId))
        {
            Debug.LogWarning($"Avatar {avatarId} not found in manifest");
            return LoadDefaultAvatar();
        }

        // If preview is already cached in memory, return it
        if (_previewCache.ContainsKey(avatarId))
        {
            return _previewCache[avatarId];
        }

        AvatarData avatarData = _avatarDictionary[avatarId];

        // For default avatar, use the resource
        if (avatarId == "default")
        {
            Texture2D defaultTexture = LoadDefaultAvatar();
            _previewCache[avatarId] = defaultTexture;
            return defaultTexture;
        }

        // Check if the full avatar is already available locally and owned
        // If so, use that instead of downloading the preview
        string localAvatarPath = GetLocalAvatarPath(avatarId);
        if (IsAvatarOwned(avatarId) && File.Exists(localAvatarPath))
        {
            try
            {
                Texture2D texture = new Texture2D(1, 1);
                byte[] fileData = await File.ReadAllBytesAsync(localAvatarPath);
                texture.LoadImage(fileData);

                // Cache in memory
                _previewCache[avatarId] = texture;
                return texture;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Error loading avatar as preview: {e.Message}");
                // Fall through to download the preview
            }
        }

        // Download preview from server (but don't save to disk)
        try
        {
            string previewPath = $"Avatars/{avatarData.path}_preview{avatarData.fileType}";
            byte[] previewData = await DownloadFileBytes(previewPath, 20 * 1024); // 20KB limit

            // Create texture from downloaded bytes
            Texture2D loadedTexture = new Texture2D(1, 1);
            loadedTexture.LoadImage(previewData);

            // Cache the preview in memory only
            _previewCache[avatarId] = loadedTexture;

            return loadedTexture;
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to download preview for {avatarId}: {e}");
            return LoadDefaultAvatar();
        }
    }

    /// <summary>
    /// Get the local file path for an avatar
    /// </summary>
    private string GetLocalAvatarPath(string avatarId)
    {
        if (!_avatarDictionary.ContainsKey(avatarId))
            return string.Empty;

        AvatarData avatarData = _avatarDictionary[avatarId];
        return Path.Combine(_localPath, $"{avatarId}{avatarData.fileType}");
    }

    /// <summary>
    /// Check if user owns the specified avatar
    /// </summary>
    public bool IsAvatarOwned(string avatarId)
    {
        if (avatarId == "default")
            return true;

        return _userData.ownedAvatars != null &&
               _userData.ownedAvatars.Contains(avatarId);
    }

    /// <summary>
    /// Check if avatar is currently equipped
    /// </summary>
    public bool IsAvatarEquipped(string avatarId)
    {
        return _userData.equippedAvatar == avatarId;
    }

    /// <summary>
    /// Ensures an owned avatar is downloaded to local storage
    /// Only downloads if the avatar is owned but not locally available
    /// </summary>
    private async Task<bool> EnsureOwnedAvatarIsDownloaded(string avatarId)
    {
        if (avatarId == "default")
            return true;

        // Only download if owned
        if (!IsAvatarOwned(avatarId))
        {
            Debug.LogWarning($"Cannot download avatar {avatarId} - not owned");
            return false;
        }

        if (!_avatarDictionary.ContainsKey(avatarId))
        {
            Debug.LogWarning($"Avatar {avatarId} not found in manifest");
            return false;
        }

        string localFilePath = GetLocalAvatarPath(avatarId);

        // Skip if already cached locally
        if (File.Exists(localFilePath))
            return true;

        try
        {
            AvatarData avatarData = _avatarDictionary[avatarId];
            string avatarPath = $"Avatars/{avatarData.path}{avatarData.fileType}";

            // Download and save to disk since it's owned
            byte[] avatarByteData = await DownloadFileBytes(avatarPath, 100 * 1024); // 100KB limit
            await File.WriteAllBytesAsync(localFilePath, avatarByteData);
            Debug.Log($"Downloaded owned avatar {avatarId} to {localFilePath}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to ensure owned avatar {avatarId} is downloaded: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Equips an avatar that the user owns
    /// </summary>
    public async Task EquipAvatar(string avatarId)
    {
        if (!IsAvatarOwned(avatarId))
        {
            Debug.LogWarning($"Cannot equip avatar {avatarId} - not owned");
            return;
        }

        // Skip if already equipped
        if (_userData.equippedAvatar == avatarId)
            return;

        // Ensure avatar is downloaded if it's not the default
        if (avatarId != "default")
        {
            bool downloaded = await EnsureOwnedAvatarIsDownloaded(avatarId);
            if (!downloaded)
            {
                Debug.LogError($"Failed to download avatar {avatarId}, cannot equip");
                return;
            }
        }

        // Update local data
        _userData.equippedAvatar = avatarId;
        _equippedAvatarId = avatarId;

        // Save to Firebase
        await FirebaseService.Instance.SaveUserAvatarData(_userData);

        // Clear texture cache to force reload with new avatar
        if (_avatarTextureCache.ContainsKey(_equippedAvatarId))
        {
            _avatarTextureCache.Remove(_equippedAvatarId);
        }

        // Notify listeners
        OnAvatarEquipped?.Invoke(avatarId);

        // Notify GameManager that avatar changed
        if (GameManager.Instance != null)
        {
            Texture2D avatarTexture = await GetCurrentAvatarTexture();
            GameManager.Instance.SetAvatar(avatarTexture, true, _equippedAvatarId);
        }

        Debug.Log($"Equipped avatar: {avatarId}");
    }

    /// <summary>
    /// Gets the texture for the currently equipped avatar
    /// </summary>
    public async Task<Texture2D> GetCurrentAvatarTexture()
    {
        return await GetAvatarTexture(_equippedAvatarId);
    }

    /// <summary>
    /// Gets the texture for a specific avatar by ID
    /// </summary>
    public async Task<Texture2D> GetAvatarTexture(string avatarId)
    {
        // Check cache first
        if (_avatarTextureCache.ContainsKey(avatarId))
        {
            return _avatarTextureCache[avatarId];
        }

        // Use default avatar if needed
        if (string.IsNullOrEmpty(avatarId) || avatarId == "default")
        {
            Texture2D defaultTexture = LoadDefaultAvatar();
            _avatarTextureCache["default"] = defaultTexture;
            return defaultTexture;
        }

        // Make sure manifest is loaded
        if (!_manifestLoaded)
        {
            await LoadAvatarManifest();
        }

        if (!_avatarDictionary.ContainsKey(avatarId))
        {
            Debug.LogWarning($"Avatar {avatarId} not found in dictionary, using default");
            return LoadDefaultAvatar();
        }

        // Check if owned - only load owned avatars from disk
        if (!IsAvatarOwned(avatarId))
        {
            Debug.LogWarning($"Avatar {avatarId} is not owned, using default");
            return LoadDefaultAvatar();
        }

        string localFilePath = GetLocalAvatarPath(avatarId);

        // If owned but not on disk, download it
        if (!File.Exists(localFilePath))
        {
            bool downloaded = await EnsureOwnedAvatarIsDownloaded(avatarId);
            if (!downloaded)
            {
                return LoadDefaultAvatar();
            }
        }

        try
        {
            // Load from local path
            Texture2D texture = new Texture2D(1, 1);
            byte[] fileData = await File.ReadAllBytesAsync(localFilePath);
            texture.LoadImage(fileData);

            // Cache the loaded texture
            _avatarTextureCache[avatarId] = texture;

            return texture;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error loading avatar texture from disk: {e.Message}");
            return LoadDefaultAvatar();
        }
    }

    /// <summary>
    /// Loads the default avatar texture
    /// </summary>
    public Texture2D LoadDefaultAvatar()
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

    /// <summary>
    /// Purchases an avatar using in-game currency
    /// Immediately downloads and saves the full avatar to disk
    /// </summary>
    public async Task<bool> PurchaseAvatar(string avatarId)
    {
        if (!_avatarDictionary.ContainsKey(avatarId))
            return false;

        // Don't repurchase already owned avatars
        if (IsAvatarOwned(avatarId))
            return true;

        try
        {
            AvatarData avatarData = _avatarDictionary[avatarId];

            // Check if user has enough currency
            if (_userData.currency < avatarData.price)
            {
                Debug.Log(
                    $"Not enough currency to purchase avatar. Have: {_userData.currency}, Need: {avatarData.price}");
                return false;
            }

            // First download the full avatar
            string avatarPath = $"Avatars/{avatarData.path}{avatarData.fileType}";
            string localFilePath = GetLocalAvatarPath(avatarId);

            // Download and save to disk
            byte[] avatarBytes = await DownloadFileBytes(avatarPath, 100 * 1024); // 100KB limit
            await File.WriteAllBytesAsync(localFilePath, avatarBytes);
            Debug.Log($"Downloaded purchased avatar {avatarId} to {localFilePath}");
            
            _userData.currency -= (int)avatarData.price;
            if (_userData.ownedAvatars == null)
                _userData.ownedAvatars = new List<string>();

            _userData.ownedAvatars.Add(avatarId);

            // Send purchase event to analytics
            UnityAnalyticsHandler.Instance.RecordPurchase(FirebaseService.Instance.UserID, avatarId, avatarData.price);
            // Save changes to database
            await FirebaseService.Instance.SaveUserAvatarData(_userData);

            // Clear the preview cache for this avatar so we'll use the full version next time
            if (_previewCache.ContainsKey(avatarId))
            {
                _previewCache.Remove(avatarId);
            }

            Debug.Log($"Successfully purchased avatar: {avatarId}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error purchasing avatar: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Clear memory caches to free up memory
    /// Called when leaving lobby scene or when memory pressure is high
    /// </summary>
    public void ClearMemoryCaches()
    {
        foreach (var texture in _previewCache.Values)
        {
            Destroy(texture);
        }

        _previewCache.Clear();

        // Keep the currently equipped texture in cache
        Dictionary<string, Texture2D> newCache = new Dictionary<string, Texture2D>();
        if (_avatarTextureCache.ContainsKey(_equippedAvatarId))
        {
            newCache[_equippedAvatarId] = _avatarTextureCache[_equippedAvatarId];
        }

        // Destroy the rest
        foreach (var entry in _avatarTextureCache)
        {
            if (entry.Key != _equippedAvatarId)
            {
                Destroy(entry.Value);
            }
        }

        _avatarTextureCache = newCache;
    }
}