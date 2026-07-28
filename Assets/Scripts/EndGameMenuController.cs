using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using Yarn.Unity;

// EndGameMenuController
//
// Shows an "Exit to Main Menu" button once the Yarn dialogue has
// completely finished — i.e. one of Prototype.yarn's Ending_<God>_<Human>
// nodes has played its line and there's nothing left to run. Hooks into
// DialogueRunner's onDialogueComplete event, which fires automatically
// in that case (none of the ending nodes need an explicit <<stop>> or
// end-of-game command for this to work).
//
// Built procedurally at runtime (find-or-create pattern, mirroring
// SplashScreenController) rather than hand-added to the scene, so
// wiring it up doesn't require any manual scene-file editing — just
// attach this component to any GameObject in YarnViabilityTest.unity
// and assign Dialogue Runner in the Inspector.
//
// SETUP:
// 1. Attach to a GameObject in YarnViabilityTest.unity (e.g. alongside
//    the other manager scripts).
// 2. Assign Dialogue Runner in the Inspector.
// 3. (Optional) Tweak Main Menu Scene Name if your splash/menu scene
//    isn't called "SplashScreen", or the button's appearance fields.

public class EndGameMenuController : MonoBehaviour
{
    [Header("References")]
    public DialogueRunner dialogueRunner;

    [Header("Scene")]
    // Must match a scene name in Build Settings — "SplashScreen" is the
    // project's existing title/menu scene (build index 0).
    public string mainMenuSceneName = "SplashScreen";

    [Header("Button Appearance")]
    // Bronze/gold to match the splash screen's own Play button.
    public string buttonLabel = "Exit to Main Menu";
    public Color buttonColor = new Color(0.72f, 0.53f, 0.15f);
    public Color buttonTextColor = Color.white;
    public TMP_FontAsset buttonFont;
    public Vector2 buttonSize = new Vector2(420f, 100f);
    public float buttonFontSize = 36f;

    private GameObject canvasObject;

    void Awake()
    {
        BuildUI();
        HideExitButton();

        if (dialogueRunner != null)
        {
            dialogueRunner.onDialogueComplete.AddListener(ShowExitButton);
        }
        else
        {
            Debug.LogWarning("EndGameMenuController: Dialogue Runner is not assigned — " +
                              "the exit-to-menu button will never appear.");
        }
    }

    void OnDestroy()
    {
        if (dialogueRunner != null)
        {
            dialogueRunner.onDialogueComplete.RemoveListener(ShowExitButton);
        }
    }

    // ── UI construction ──────────────────────────────────────────────────

    private void BuildUI()
    {
        canvasObject = FindOrCreateChild(transform, "EndGameMenuCanvas");

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = canvasObject.AddComponent<Canvas>();
        }
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above the gameplay canvas and the Yarn dialogue canvas, so it's
        // never accidentally hidden behind the dialogue box or portraits.
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            scaler = canvasObject.AddComponent<CanvasScaler>();
        }
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        if (canvasObject.GetComponent<GraphicRaycaster>() == null)
        {
            canvasObject.AddComponent<GraphicRaycaster>();
        }

        GameObject buttonObject = FindOrCreateChild(canvasObject.transform, "ExitToMainMenuButton");

        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        if (buttonRect == null) buttonRect = buttonObject.AddComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.anchoredPosition = Vector2.zero;
        buttonRect.sizeDelta = buttonSize;

        Image buttonImage = buttonObject.GetComponent<Image>();
        if (buttonImage == null) buttonImage = buttonObject.AddComponent<Image>();
        buttonImage.color = buttonColor;

        // Bevelled edge: a light offset copy peeking out top-left plus a
        // dark offset copy peeking out bottom-right, on a plain flat-color
        // Image, reads as a subtle embossed/bevelled border — same trick
        // used on the splash screen's own Play button.
        Shadow bevelHighlight = buttonObject.GetComponent<Shadow>();
        if (bevelHighlight == null) bevelHighlight = buttonObject.AddComponent<Shadow>();
        bevelHighlight.effectColor = new Color(1f, 1f, 1f, 0.55f);
        bevelHighlight.effectDistance = new Vector2(-2f, 2f);
        bevelHighlight.useGraphicAlpha = false;

        Shadow[] existingShadows = buttonObject.GetComponents<Shadow>();
        Shadow bevelShadow = existingShadows.Length > 1 ? existingShadows[1] : buttonObject.AddComponent<Shadow>();
        bevelShadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
        bevelShadow.effectDistance = new Vector2(2f, -2f);
        bevelShadow.useGraphicAlpha = false;

        Button button = buttonObject.GetComponent<Button>();
        if (button == null) button = buttonObject.AddComponent<Button>();
        button.onClick.RemoveListener(OnExitButtonClicked);
        button.onClick.AddListener(OnExitButtonClicked);

        GameObject labelObject = FindOrCreateChild(buttonObject.transform, "Label");

        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        if (labelRect == null) labelRect = labelObject.AddComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        if (label == null) label = labelObject.AddComponent<TextMeshProUGUI>();
        label.text = buttonLabel;
        label.color = buttonTextColor;
        label.fontSize = buttonFontSize;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        if (buttonFont != null) label.font = buttonFont;
    }

    private GameObject FindOrCreateChild(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null) return existing.gameObject;

        GameObject created = new GameObject(name);
        created.transform.SetParent(parent, false);
        return created;
    }

    // ── Show / hide / click ──────────────────────────────────────────────

    private void ShowExitButton()
    {
        if (canvasObject != null) canvasObject.SetActive(true);
    }

    private void HideExitButton()
    {
        if (canvasObject != null) canvasObject.SetActive(false);
    }

    private void OnExitButtonClicked()
    {
        SceneManager.LoadScene(mainMenuSceneName);
    }
}
