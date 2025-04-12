using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages the Avatar Shop UI which is only needed in the lobby scene
/// </summary>
public class AvatarShopManager : MonoBehaviour
{
    [SerializeField] private GameObject shopCanvas;
    [SerializeField] private Transform avatarItemsContainer;
    [SerializeField] private TextMeshProUGUI currencyText;
    [SerializeField] private Button openShopButton;
    [SerializeField] private Button closeShopButton;
    [SerializeField] private Button exitButton;

    private GameObject avatarPreviewPrefab;
    private bool _shopInitialized = false;
    private bool _isPurchasing = false;
    private bool _isRefreshing = false;

    private void Start()
    {
        // Hide shop canvas by default
        if (shopCanvas != null)
        {
            shopCanvas.SetActive(false);
        }

        // Setup shop UI if present
        if (openShopButton != null)
        {
            openShopButton.onClick.AddListener(OpenShop);
        }

        if (closeShopButton != null)
        {
            closeShopButton.onClick.AddListener(CloseShop);
        }

        if (exitButton != null)
        {
            exitButton.onClick.AddListener(QuitGame);
        }

        // Load avatar preview prefab if not set
        if (avatarPreviewPrefab == null)
        {
            avatarPreviewPrefab = Resources.Load<GameObject>("Shop/AvatarPreview");
            if (avatarPreviewPrefab == null)
            {
                Debug.LogError("Could not find AvatarPreview prefab in Resources/Shop folder!");
            }
        }

        // Subscribe to user data events
        FirebaseService.OnUserDataLoaded += OnUserDataLoaded;
        FirebaseStorageHandler.OnAvatarEquipped += OnAvatarEquipped;
    }

    private void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void OnDestroy()
    {
        // Unsubscribe from events
        FirebaseService.OnUserDataLoaded -= OnUserDataLoaded;
        FirebaseStorageHandler.OnAvatarEquipped -= OnAvatarEquipped;
    }

    private void OnAvatarEquipped(string avatarId)
    {
        // Refresh shop UI to update equipped status
        if (shopCanvas != null && shopCanvas.activeSelf && _shopInitialized)
        {
            RefreshShopUI();
        }
    }

    private void OnUserDataLoaded(UserAvatarData userData)
    {
        // Update currency display
        UpdateCurrencyDisplay();

        // If shop is open, refresh it
        if (shopCanvas != null && shopCanvas.activeSelf && _shopInitialized)
        {
            RefreshShopUI();
        }
    }

    /// <summary>
    /// Updates the currency display in the UI
    /// </summary>
    private void UpdateCurrencyDisplay()
    {
        if (currencyText != null)
        {
            currencyText.text = $"{FirebaseStorageHandler.Instance.UserData.currency} COINS";
        }
    }

    /// <summary>
    /// Opens the avatar shop UI
    /// </summary>
    void OpenShop()
    {
        if (shopCanvas != null)
        {
            openShopButton.gameObject.SetActive(false);
            shopCanvas.SetActive(true);
            UpdateCurrencyDisplay();

            if (!_shopInitialized)
            {
                StartCoroutine(InitializeShopCoroutine());
            }
        }
    }

    /// <summary>
    /// Closes the avatar shop UI
    /// </summary>
    void CloseShop()
    {
        if (shopCanvas != null)
        {
            shopCanvas.SetActive(false);
            openShopButton.gameObject.SetActive(true);
        }
    }

    private IEnumerator InitializeShopCoroutine()
    {
        // Ensure the avatar manifest is loaded
        Task<bool> manifestTask = FirebaseStorageHandler.Instance.LoadAvatarManifest();
        yield return new WaitUntil(() => manifestTask.IsCompleted);

        if (!manifestTask.Result)
        {
            Debug.LogError("Failed to load avatar manifest");
            yield break;
        }

        // Now populate the shop UI
        _shopInitialized = true;
        RefreshShopUI();
    }

    /// <summary>
    /// Refreshes the shop UI with current avatar data
    /// </summary>
    private void RefreshShopUI()
    {
        if (_isRefreshing)
            return;

        _isRefreshing = true;
        StartCoroutine(RefreshShopUICoroutine());
    }

    private IEnumerator RefreshShopUICoroutine()
    {
        // Clear any existing items
        foreach (Transform child in avatarItemsContainer)
        {
            Destroy(child.gameObject);
        }

        // Add items for each avatar
        int index = 0;
        foreach (var avatarEntry in FirebaseStorageHandler.Instance.AvatarDictionary)
        {
            // Skip default avatar if it's not in the shop
            if (avatarEntry.Key == "default")
                continue;

            yield return StartCoroutine(CreateAvatarItem(avatarEntry.Key, avatarEntry.Value, index));
            index++;
        }

        _isRefreshing = false;
    }

    /// <summary>
    /// Creates a UI item for an avatar
    /// </summary>
    private IEnumerator CreateAvatarItem(string avatarId, AvatarData avatarData, int index)
    {
        // Get or download the preview image
        Task<Texture2D> previewTask = FirebaseStorageHandler.Instance.GetAvatarPreview(avatarId);
        yield return new WaitUntil(() => previewTask.IsCompleted);

        if (previewTask.IsFaulted)
        {
            Debug.LogError($"Failed to load preview for {avatarId}: {previewTask.Exception}");
            yield break;
        }

        // Create preview item in shop UI
        Texture2D texture = previewTask.Result;
        GameObject previewObject = Instantiate(avatarPreviewPrefab, avatarItemsContainer);

        // Setup the preview image
        Image previewImage = previewObject.GetComponent<Image>();
        previewImage.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
            new Vector2(0.5f, 0.5f));

        // Check ownership status
        bool isOwned = FirebaseStorageHandler.Instance.IsAvatarOwned(avatarId);
        bool isEquipped = FirebaseStorageHandler.Instance.IsAvatarEquipped(avatarId);

        // Setup UI elements based on ownership status
        Button button = previewObject.GetComponent<Button>();
        TextMeshProUGUI buttonText = button.GetComponentInChildren<TextMeshProUGUI>();

        // Set the avatar name or ID if there's a label component
        TextMeshProUGUI nameLabel = previewObject.transform.Find("AvatarName")?.GetComponent<TextMeshProUGUI>();
        if (nameLabel != null)
        {
            nameLabel.text = avatarId;
        }

        // Clear previous event listeners
        button.onClick.RemoveAllListeners();

        // Apply color based on status
        if (isEquipped)
        {
            buttonText.text = "EQUIPPED";
            button.interactable = false;
        }
        else if (isOwned)
        {
            buttonText.text = "EQUIP";
            button.onClick.AddListener(() => EquipAvatar(avatarId));
        }
        else
        {
            buttonText.text = $"{avatarData.price} COINS";
            button.onClick.AddListener(() => PurchaseAvatar(avatarId));

            // Disable button if not enough currency
            if (FirebaseStorageHandler.Instance.UserData.currency < avatarData.price)
            {
                button.interactable = false;
            }
        }

        // Position the item in the top-left corner
        RectTransform rectTransform = previewObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0, 1); // Top-left anchor
        rectTransform.anchorMax = new Vector2(0, 1); // Top-left anchor
        rectTransform.pivot = new Vector2(0, 1); // Top-left pivot

        float itemHeight = 200f;
        float itemWidth = 200f;
        int itemsPerRow = 5;
        
        // Calculate row and column
        int row = index / itemsPerRow;
        int col = index % itemsPerRow;

        rectTransform.anchoredPosition = new Vector2(col * itemWidth, -(row * itemHeight));
        
    }

    /// <summary>
    /// Handles avatar purchase button click
    /// </summary>
    public async void PurchaseAvatar(string avatarId)
    {
        if (_isPurchasing)
            return;

        _isPurchasing = true;

        try
        {
            // Try to purchase the avatar
            bool success = await FirebaseStorageHandler.Instance.PurchaseAvatar(avatarId);

            if (success)
            {
                // Update currency display
                UpdateCurrencyDisplay();

                // Refresh shop UI
                RefreshShopUI();
            }
        }
        finally
        {
            _isPurchasing = false;
        }
    }

    /// <summary>
    /// Handles avatar equip button click
    /// </summary>
    public async void EquipAvatar(string avatarId)
    {
        await FirebaseStorageHandler.Instance.EquipAvatar(avatarId);

        // The UI refresh will be handled by the OnAvatarEquipped event
    }
}