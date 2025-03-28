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
    // public string name;
    // public string description;
    public string path;
    public string fileType;
    public float price;
}

public class FirebaseStorageHandler : MonoBehaviourSingleton<FirebaseStorageHandler>
{
    private FirebaseStorage _storage;
    private StorageReference _ref;

    private string _localPath;

    Dictionary<string, AvatarData> _avatarDictionary = new Dictionary<string, AvatarData>();
    Dictionary<string, Texture2D> _previewCache = new Dictionary<string, Texture2D>();
    Dictionary<string, AvatarData> _ownedAvatars = new Dictionary<string, AvatarData>();

    private AvatarData activeAvatar;

    Transform _previewContainer;
    
    public GameObject previewObjectPrefab;

    bool purchasing = false;
    string selectedAvatarID;

    public string SelectedAvatarID => selectedAvatarID;

    private void Awake()
    {
        if (Instance != this)
        {
            Destroy(gameObject);
        }

        DontDestroyOnLoad(gameObject);

        _localPath = Path.Combine(Application.persistentDataPath, "Avatars");
        if (!Directory.Exists(_localPath))
        {
            Directory.CreateDirectory(_localPath);
        }
    }

    private void Start()
    {
        // gs://cg-mcast.firebasestorage.app/
        _storage = FirebaseStorage.DefaultInstance;
        _ref = _storage.RootReference;
        _previewContainer = GameObject.FindWithTag("ShopItems").transform;
        _previewContainer.parent.gameObject.SetActive(false);
        previewObjectPrefab = Resources.Load<GameObject>("Shop/AvatarPreview");
    }

    async Task LoadAvatarManifest()
    {
        if(_avatarDictionary.Count > 0)
            return;
        
        try
        {
            byte[] manifestData = await DownloadFileBytes("Avatars/manifest.json", 5 * 1024); // 5KB
            ProcessManifest(manifestData);
        }
        catch (Exception e)
        {
            Debug.LogError("Failed to load avatar manifest: " + e);
        }
    }

    async Task<byte[]> DownloadFileBytes(string path, int size)
    {
        StorageReference fileRef = _ref.Child(path);
        return await fileRef.GetBytesAsync(size);
    }

    void ProcessManifest(byte[] data)
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
            StartCoroutine(LoadPreview(avatarData, (byte)i));
        }
    }

    async Task<Texture2D> LoadImageFromDisk(string path)
    {
        byte[] imageData = await File.ReadAllBytesAsync(path);
        Texture2D texture = new Texture2D(1, 1);
        texture.LoadImage(imageData);
        return texture;
    }

    IEnumerator LoadPreview(AvatarData avatarData, byte idx)
    {
        string previewPath = $"Avatars/{avatarData.path}_preview{avatarData.fileType}";

        if (!_previewCache.ContainsKey(avatarData.id))
        {
            // Get the preview image from firebase storage
            Task<byte[]> downloadTask = DownloadFileBytes(previewPath, 20 * 1024); // 20KB
            yield return new WaitUntil(() => downloadTask.IsCompleted);
            // turn downloadTask into a texture
            Texture2D loadedTexture = new Texture2D(1, 1);
            loadedTexture.LoadImage(downloadTask.Result);
            _previewCache.Add(avatarData.id, loadedTexture);
        }

        Texture2D texture = _previewCache[avatarData.id];
        GameObject previewObject = Instantiate(previewObjectPrefab, _previewContainer);
        previewObject.GetComponent<Image>().sprite = Sprite.Create(texture,
            new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        previewObject.GetComponent<Button>().onClick.AddListener(() => { PurchaseAvatar(avatarData.id); });
        RectTransform rectTransform = previewObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0, 1);
        rectTransform.anchorMax = new Vector2(0, 1);
        rectTransform.pivot = new Vector2(0, 1);
        rectTransform.anchoredPosition = Vector2.zero;
        // Move to the right based on the idx provided
        rectTransform.anchoredPosition += new Vector2(0, -idx * 200);

        TextMeshProUGUI priceText = previewObject.GetComponentInChildren<TextMeshProUGUI>();
        priceText.text = $"{avatarData.price}sc";
    }

    void PurchaseAvatar(string id)
    {
        if (purchasing)
            return;

        purchasing = true;

        if (_avatarDictionary.ContainsKey(id))
        {
            Debug.Log($"User already owns avatar {id}");
            purchasing = false;
            selectedAvatarID = id;
            return;
        }

        // Purchase logic
        float cost = _avatarDictionary[id].price;
        if (cost > 0) // Check user currency
        {
            Debug.Log($"Purchased avatar {id} for {cost}sc");
            string avatarPath =
                Path.Combine($"Avatars/{_avatarDictionary[id].path}{_avatarDictionary[id].fileType}");
            Task downloadTask = DownloadAvatar(id, avatarPath);
            return;
        }

        purchasing = false;
    }

    async Task DownloadAvatar(string id, string avatarPath)
    {
        try
        {
            byte[] avatarData = await DownloadFileBytes($"Avatars/{avatarPath}", 50 * 1024); // 50KB
            await File.WriteAllBytesAsync(Path.Combine(_localPath, avatarPath), avatarData);
            _ownedAvatars.Add(id, _avatarDictionary[id]);
        } catch (ArgumentException e)
        {
            Debug.LogError("Failed to add avatar to owned avatars: " + e);
        }
    }

    public async Task<Texture2D> LoadAvatarImage(string path)
    {
        string localPath = Path.Combine(_localPath, path);
        if (!File.Exists(localPath))
        {
            // Download the avatar
            byte[] avatarData = await DownloadFileBytes($"Avatars/{path}", 50 * 1024); // 50KB
            Texture2D texture = new Texture2D(1, 1);
            texture.LoadImage(avatarData);
            return texture;
        }

        return await LoadImageFromDisk(localPath);
    }
}