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

    public void Show(CaseInfo info)
    {
        if (info == null) return;

        if (titleText)       titleText.text       = info.caseTitle;
        if (statusText)      statusText.text       = $"[ {info.statusBadge} ]";
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
