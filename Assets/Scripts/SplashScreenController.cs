using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

// SplashScreenController
//
// Builds the game's splash/title screen entirely at runtime — Canvas,
// background, character art, title text and Play button are all
// created procedurally in code, the same self-contained approach used
// by this project's runtime-generated visual effects. No scene-editing
// required beyond the one-time setup below.
//
// SETUP IN UNITY:
// 1. Create a new empty Scene (e.g. "SplashScreen") and add it to
//    Build Settings ABOVE your gameplay scene, so it loads first.
// 2. Also make sure your gameplay scene (e.g. "YarnViabilityTest") is
//    in Build Settings, since the Play button loads it by name.
// 3. In the new scene, attach this script to an empty GameObject.
// 4. In the Inspector assign:
//      - Background Sprite               → Assets/Sprites/Background/TempleDayBackground.png
//      - Meritamun Worried Sprite         → Assets/Sprites/PlayerCharacterMeritamun/Meritamun_Worried.png
//      - Cat Meritamun Paw Raised Sprite  → Assets/Sprites/PlayerCharacterMeritamun/Cat_Meritamun_pawraised.png
// 5. Adjust Gameplay Scene Name if your main scene isn't named
//    "YarnViabilityTest".

public class SplashScreenController : MonoBehaviour
{
    [Header("Sprites")]
    public Sprite backgroundSprite;
    public Sprite meritamunWorriedSprite;
    public Sprite catMeritamunPawRaisedSprite;

    [Header("Font")]
    // Applied to both the title and the Play button label. Leave blank
    // to fall back to TextMeshPro's default project font.
    public TMP_FontAsset gameFont;

    [Header("Title")]
    public string gameTitle = "BAST";
    public int titleFontSize = 180;
    public Color titleColor = new Color(0.95f, 0.85f, 0.55f); // warm gold, matches the game's palette

    [Header("Play Button")]
    public string playButtonLabel = "Play";
    public Color playButtonColor = new Color(0.72f, 0.53f, 0.15f);
    public string gameplaySceneName = "YarnViabilityTest";

    void Start()
    {
        EnsureEventSystem();
        BuildSplashScreen();
    }

    // ── Setup ────────────────────────────────────────────────────────────

    // A fresh scene has no EventSystem of its own — without one, the
    // Play button can't receive clicks at all.
    private void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null) return;

        GameObject eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<InputSystemUIInputModule>();
    }

    private void BuildSplashScreen()
    {
        GameObject canvasObject = new GameObject("SplashCanvas", typeof(RectTransform));
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObject.AddComponent<GraphicRaycaster>();

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();

        CreateBackground(canvasRect);
        CreateCharacterImage(canvasRect, meritamunWorriedSprite, "MeritamunWorried",
            anchor: new Vector2(0f, 0f), size: new Vector2(450f, 700f), offset: new Vector2(60f, 0f));
        CreateCharacterImage(canvasRect, catMeritamunPawRaisedSprite, "CatMeritamunPawRaised",
            anchor: new Vector2(1f, 0f), size: new Vector2(450f, 700f), offset: new Vector2(-60f, 0f));
        CreateTitle(canvasRect);
        CreatePlayButton(canvasRect);
    }

    // ── Elements ─────────────────────────────────────────────────────────

    private void CreateBackground(RectTransform parent)
    {
        if (backgroundSprite == null) return;

        GameObject bgObject = new GameObject("Background", typeof(RectTransform));
        RectTransform rect = bgObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;

        Image image = bgObject.AddComponent<Image>();
        image.sprite = backgroundSprite;
        image.preserveAspect = false; // fill the screen edge to edge
        image.raycastTarget = false;
    }

    // Anchors, sizes and positions a character portrait at a screen
    // corner. Skips gracefully if no sprite has been assigned yet.
    private void CreateCharacterImage(RectTransform parent, Sprite sprite, string name, Vector2 anchor, Vector2 size, Vector2 offset)
    {
        if (sprite == null) return;

        GameObject imageObject = new GameObject(name, typeof(RectTransform));
        RectTransform rect = imageObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.sizeDelta = size;
        rect.anchoredPosition = offset;

        Image image = imageObject.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
    }

    private void CreateTitle(RectTransform parent)
    {
        GameObject titleObject = new GameObject("TitleText", typeof(RectTransform));
        RectTransform rect = titleObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(1400f, 300f);
        rect.anchoredPosition = new Vector2(0f, -120f);

        TextMeshProUGUI text = titleObject.AddComponent<TextMeshProUGUI>();
        text.text = gameTitle;
        text.fontSize = titleFontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.color = titleColor;
        text.fontStyle = FontStyles.Bold;
        text.raycastTarget = false;
        if (gameFont != null) text.font = gameFont;
    }

    private void CreatePlayButton(RectTransform parent)
    {
        GameObject buttonObject = new GameObject("PlayButton", typeof(RectTransform));
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(320f, 100f);
        rect.anchoredPosition = new Vector2(0f, 90f);

        Image buttonImage = buttonObject.AddComponent<Image>();
        buttonImage.color = playButtonColor;

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = buttonImage;
        button.onClick.AddListener(OnPlayButtonClicked);

        GameObject labelObject = new GameObject("Label", typeof(RectTransform));
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.SetParent(rect, false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.sizeDelta = Vector2.zero;
        labelRect.anchoredPosition = Vector2.zero;

        TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
        label.text = playButtonLabel;
        label.fontSize = 44;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.fontStyle = FontStyles.Bold;
        label.raycastTarget = false;
        if (gameFont != null) label.font = gameFont;
    }

    private void OnPlayButtonClicked()
    {
        SceneManager.LoadScene(gameplaySceneName);
    }
}
