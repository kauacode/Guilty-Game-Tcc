using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DossierPanel : MonoBehaviour
{
    [Header("Painel")]
    [SerializeField] private RectTransform panelRect;
    [SerializeField] private CanvasGroup   panelGroup;
    [SerializeField] private float         panelWidth = 560f;

    [Header("Conteúdo")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private Image           suspectImage;
    [SerializeField] private TextMeshProUGUI descriptionText;

    [Header("Botões")]
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button backButton;

    public event System.Action OnConfirmRequested;
    public event System.Action OnBackRequested;

    public bool IsVisible { get; private set; }

    /// <summary>
    /// O clique cai no botão? Teste por posição, sem depender do EventSystem —
    /// é o que permite o controlador distinguir "clicou em INICIAR" de "clicou
    /// em qualquer outro lugar".
    /// </summary>
    public bool IsOverConfirm(Vector2 screenPoint) => Contains(confirmButton, screenPoint);
    public bool IsOverBack(Vector2 screenPoint) => Contains(backButton, screenPoint);

    private static bool Contains(Button b, Vector2 p)
    {
        if (b == null || !b.gameObject.activeInHierarchy) return false;
        var canvas = b.GetComponentInParent<Canvas>();
        Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            ? canvas.worldCamera : null;
        return RectTransformUtility.RectangleContainsScreenPoint((RectTransform)b.transform, p, cam);
    }

    private void Start()
    {
        if (panelRect != null)
            panelRect.anchoredPosition = new Vector2(-panelWidth, 0f);

        if (panelGroup != null)
        {
            panelGroup.alpha          = 0f;
            panelGroup.interactable   = false;
            panelGroup.blocksRaycasts = false;
        }

        confirmButton?.onClick.AddListener(() => OnConfirmRequested?.Invoke());
        backButton?.onClick.AddListener(() => OnBackRequested?.Invoke());
    }

    private const string LockedBadge = "DOSSIÊ LACRADO";

    private TMP_Text confirmLabel;
    private string   confirmLabelText;
    private Color    confirmLabelColor;

    public void Show(CaseInfo info) => Show(info, null);

    /// <summary>
    /// <paramref name="lockedReason"/> != null: caso bloqueado. O dossiê abre
    /// para leitura, mas o INICIAR fica desabilitado e diz o que falta. Quem
    /// impede de fato a partida é o BoardSelectionController (ENTER e o clique
    /// por posição não passam pelo onClick do botão).
    /// </summary>
    public void Show(CaseInfo info, string lockedReason)
    {
        if (info == null) return;
        bool locked = lockedReason != null;

        if (titleText)       titleText.text       = info.caseTitle;
        // O selo da cena de um caso ainda bloqueado pode dizer "AGUARDANDO
        // INTERROGATÓRIO"; enquanto não abrir, ele é lacrado.
        if (statusText)      statusText.text       = $"[ {(locked ? LockedBadge : info.statusBadge)} ]";
        ApplyConfirmLock(lockedReason);
        if (descriptionText) descriptionText.text  = info.caseDescription;
        if (suspectImage)
        {
            suspectImage.sprite  = info.suspectPhoto;
            suspectImage.color   = info.suspectPhoto != null
                ? Color.white
                : new Color(0.15f, 0.12f, 0.09f);
        }

        IsVisible = true;
        StopAllCoroutines();
        StartCoroutine(Animate(show: true));
    }

    private void ApplyConfirmLock(string lockedReason)
    {
        if (confirmButton == null) return;
        if (confirmLabel == null)
        {
            // Guarda o texto e a cor originais para voltar a eles quando o
            // próximo dossiê aberto for de um caso disponível.
            confirmLabel = confirmButton.GetComponentInChildren<TMP_Text>(true);
            if (confirmLabel != null)
            {
                confirmLabelText  = confirmLabel.text;
                confirmLabelColor = confirmLabel.color;
            }
        }

        bool locked = lockedReason != null;
        confirmButton.interactable = !locked;
        if (confirmLabel == null) return;
        confirmLabel.text  = locked ? $"BLOQUEADO\n<size=80%>{lockedReason}</size>" : confirmLabelText;
        confirmLabel.color = locked ? new Color(confirmLabelColor.r, confirmLabelColor.g, confirmLabelColor.b, 0.6f)
                                    : confirmLabelColor;
    }

    public void Hide()
    {
        IsVisible = false;
        StopAllCoroutines();
        StartCoroutine(Animate(show: false));
    }

    private IEnumerator Animate(bool show)
    {
        float targetX     = show ? 0f : -panelWidth;
        float targetAlpha = show ? 1f : 0f;

        if (show && panelGroup != null)
        {
            panelGroup.interactable   = true;
            panelGroup.blocksRaycasts = true;
        }

        float   elapsed    = 0f;
        const float duration = 0.32f;
        Vector2 startPos   = panelRect  != null ? panelRect.anchoredPosition : Vector2.zero;
        float   startAlpha = panelGroup != null ? panelGroup.alpha : (show ? 0f : 1f);

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float k  = Mathf.Clamp01(elapsed / duration);
            k = k * k * (3f - 2f * k); // smoothstep

            if (panelRect  != null) panelRect.anchoredPosition = new Vector2(Mathf.Lerp(startPos.x, targetX, k), 0f);
            if (panelGroup != null) panelGroup.alpha            = Mathf.Lerp(startAlpha, targetAlpha, k);
            yield return null;
        }

        if (panelRect  != null) panelRect.anchoredPosition = new Vector2(targetX, 0f);
        if (panelGroup != null) panelGroup.alpha            = targetAlpha;

        if (!show && panelGroup != null)
        {
            panelGroup.interactable   = false;
            panelGroup.blocksRaycasts = false;
        }
    }
}
