using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using UnityEngine.UI;

public class Cell : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public TMP_Text hieroglyphText;  // drag HieroglyphText here in Inspector
    public string hieroglyphValue;   // stores which hieroglyph this cell holds

    [Range(0f, 1f)] public float hoverDarkenFactor = 0.7f; // multiplies RGB while hovered — lower = darker

    private Button button;
    private Image background;


    public int row;
    public int col;

    private Color originalColor;       // store the starting colour here
    private Color currentBaseColor;    // whatever colour the game logic last set via SetHighlight/ResetColour
    private bool isHovered = false;
    private GridManager gridManager;

    public bool isPartOfFoundWord = false;

    void Start()
    {
        button = GetComponent<Button>();
        background = GetComponent<Image>();

        // Find the GridManager in the scene automatically
        //gridManager = FindObjectOfType<GridManager>();

        gridManager = FindAnyObjectByType<GridManager>();

        // Store the original colour the moment the game starts
        // so we always know what to revert to
        originalColor = background.color;
        currentBaseColor = originalColor;

        button.onClick.AddListener(OnCellClicked);
    }

    public void SetHieroglyph(string symbol)
    {
        hieroglyphValue = symbol;
        hieroglyphText.text = symbol;
    }

    public void SetHighlight(Color colour)
    {
        currentBaseColor = colour;
        ApplyDisplayColor();
    }

    public void ResetColour()
    {
        // Revert to the exact colour it started with
        currentBaseColor = originalColor;
        ApplyDisplayColor();
    }

    void OnCellClicked()
    {
        // Tell the GridManager this cell was clicked
    gridManager.OnCellSelected(this);
    }

    // ── Hover darkening ──────────────────────────────────────────────────

    public void OnPointerEnter(PointerEventData eventData)
    {
        // Only darken while the puzzle is actually active, and never on
        // a cell that's already locked in as part of a found word.
        if (gridManager != null && !gridManager.inputEnabled) return;
        if (isPartOfFoundWord) return;

        isHovered = true;
        ApplyDisplayColor();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;
        ApplyDisplayColor();
    }

    // Renders currentBaseColor darkened by hoverDarkenFactor while
    // hovered, without ever losing track of what the "real" colour
    // (selected / found / normal) is underneath.
    private void ApplyDisplayColor()
    {
        if (isHovered)
        {
            background.color = new Color(
                currentBaseColor.r * hoverDarkenFactor,
                currentBaseColor.g * hoverDarkenFactor,
                currentBaseColor.b * hoverDarkenFactor,
                currentBaseColor.a);
        }
        else
        {
            background.color = currentBaseColor;
        }
    }
}