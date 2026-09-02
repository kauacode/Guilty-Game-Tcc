using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// Controla toda a UI do MVP.
/// Liga os eventos da API com os elementos visuais.
/// </summary>
public class UIController : MonoBehaviour
{
    [Header("Elementos de Input")]
    [SerializeField] private TMP_InputField playerInputField;
    [SerializeField] private Button sendButton;

    [Header("Reiniciar")]
    [Tooltip("Fica escondido durante a partida e só aparece no fim de jogo — " +
             "um botão de reset sempre visível num interrogatório convida a clique acidental.")]
    [SerializeField] private Button restartButton;
    [SerializeField] private string openingLine =
        "Detetive Marcos Silva está esperando seu depoimento.\n\n" +
        "Onde você estava na noite do dia 14 de março?";

    [Header("Elementos de Output")]
    [SerializeField] private TMP_Text detectiveText;
    [SerializeField] private TMP_Text statusText;

    [Header("HUD — Nível de Suspeita")]
    [Tooltip("Barra fina no topo da tela. Preenchida via RectTransform.anchorMax.x (0=vazia, 1=cheia) em vez de Image.fillAmount — evita depender de sprite/Image Type=Filled, que exige um sprite válido para funcionar.")]
    [SerializeField] private Image suspicionFillImage;

    [Header("Feedback de Loading")]
    [SerializeField] private GameObject loadingIndicator;
    [SerializeField] private CanvasGroup canvasGroup; // para bloquear UI

    private ScrollRect detectiveScrollRect;

    private void Start()
    {
        // Conecta o botão ao método de envio
        sendButton.onClick.AddListener(OnSendButtonClicked);

        // Permite enviar com Enter
        playerInputField.onSubmit.AddListener((_) => OnSendButtonClicked());

        // Subscreve nos eventos do ApiClient
        if (ApiClient.Instance != null)
        {
            ApiClient.Instance.OnResponseReceived += HandleApiResponse;
            ApiClient.Instance.OnError += HandleApiError;
            ApiClient.Instance.OnRequestStarted += HandleRequestStarted;
            ApiClient.Instance.OnRequestFinished += HandleRequestFinished;
        }

        if (restartButton != null)
        {
            restartButton.onClick.AddListener(OnRestartButtonClicked);
            restartButton.gameObject.SetActive(false);
        }

        // Limpa texto residual do editor no campo de input (ex: texto de teste)
        playerInputField.text = "";

        // Escuta abertura do painel para limpar o campo de input
        InterrogationUIToggle.OnMenuToggled += OnPanelToggled;

        // Configura scroll do texto do detetive e melhorias visuais da HUD
        SetupDetectiveTextScrolling();
        SetupSuspicionBarBorder();
        SetupHUDSizes();

        // Estado inicial da UI
        SetLoadingState(false);
        detectiveText.text = openingLine;
        ScrollDetectiveTextToBottom();
        UpdateStatusText();

        SetSuspicionFillAmount(0f);
    }

    /// <summary>Volta a partida ao estado inicial sem recarregar a cena.</summary>
    private void OnRestartButtonClicked()
    {
        if (GameManager.Instance != null) GameManager.Instance.ResetGame();

        detectiveText.text = openingLine;
        ScrollDetectiveTextToBottom();
        playerInputField.text = "";
        sendButton.interactable = true;
        SetSuspicionFillAmount(0f);
        UpdateStatusText();

        if (restartButton != null) restartButton.gameObject.SetActive(false);

        playerInputField.ActivateInputField();
    }

    private void OnDestroy()
    {
        InterrogationUIToggle.OnMenuToggled -= OnPanelToggled;

        if (ApiClient.Instance != null)
        {
            ApiClient.Instance.OnResponseReceived -= HandleApiResponse;
            ApiClient.Instance.OnError -= HandleApiError;
            ApiClient.Instance.OnRequestStarted -= HandleRequestStarted;
            ApiClient.Instance.OnRequestFinished -= HandleRequestFinished;
        }
    }

    // Quando o painel abre, limpa o campo, ativa o foco e rola para o fim do texto
    private void OnPanelToggled(bool isOpen)
    {
        if (!isOpen) return;
        playerInputField.text = "";
        playerInputField.ActivateInputField();
        // Aguarda um frame para o layout recalcular antes de rolar
        StartCoroutine(ScrollToBottomNextFrame());
    }

    private System.Collections.IEnumerator ScrollToBottomNextFrame()
    {
        yield return null; // espera o layout ser recalculado
        ScrollDetectiveTextToBottom();
    }

    private void OnSendButtonClicked()
    {
        string text = playerInputField.text.Trim();

        if (string.IsNullOrEmpty(text))
        {
            detectiveText.text = "[Sistema] Digite algo antes de enviar.";
            return;
        }

        if (GameManager.Instance.IsGameOver)
        {
            detectiveText.text = "[Sistema] O jogo terminou. Clique em Reiniciar.";
            return;
        }

        // Chama a API via GameManager → ApiClient
        string sessionId = GameManager.Instance.SessionId;
        ApiClient.Instance.SendTestimony(sessionId, text);

        // Limpa o campo de input após envio
        playerInputField.text = "";
        playerInputField.ActivateInputField(); // Mantém foco
    }

    private void HandleApiResponse(AnalyzeResponse response)
    {
        // Aplica o resultado no estado do jogo
        GameManager.Instance.ApplyTurnResult(response);

        // Atualiza o texto do detetive
        detectiveText.text = $"<b>Turno {response.id_turno} — Detetive Silva:</b>\n\n{response.texto_detetive}";
        ScrollDetectiveTextToBottom();

        // Atualiza a barra de suspeita (HUD)
        SetSuspicionFillAmount(response.status_investigacao.nivel_suspeita / 100f);

        // Atualiza texto de status
        UpdateStatusText(response);

        // Aplica feedback visual (cor do painel)
        ApplyVisualFeedback(response.feedback_visual);

        // Verifica game over
        if (response.status_investigacao.fim_de_jogo)
        {
            detectiveText.text += "\n\n<color=#FF4444><b>— INVESTIGAÇÃO ENCERRADA —</b></color>";
            ScrollDetectiveTextToBottom();
            sendButton.interactable = false;
            if (restartButton != null) restartButton.gameObject.SetActive(true);
        }
    }

    private void HandleApiError(string errorMessage)
    {
        detectiveText.text = $"<color=#FF4444>[Erro] {errorMessage}</color>\n\nVerifique se o servidor está rodando.";
        ScrollDetectiveTextToBottom();
        Debug.LogError($"[UIController] Erro da API: {errorMessage}");
    }

    private void HandleRequestStarted()
    {
        SetLoadingState(true);
        detectiveText.text = "O detetive está analisando seu depoimento...";
        ScrollDetectiveTextToBottom();
    }

    private void HandleRequestFinished()
    {
        SetLoadingState(false);
    }

    private void SetLoadingState(bool isLoading)
    {
        if (loadingIndicator != null)
            loadingIndicator.SetActive(isLoading);

        // Bloqueia a UI durante o loading. NÃO mexe em canvasGroup.alpha:
        // esse CanvasGroup também é usado por InterrogationUIToggle para o
        // fade de abrir/fechar o painel, e as duas lógicas escrevendo no
        // mesmo alpha entravam em conflito (a UI ficava presa num estado
        // intermediário em vez de fechar 100%).
        if (canvasGroup != null)
        {
            canvasGroup.interactable = !isLoading;
        }

        sendButton.interactable = !isLoading;
    }

    /// <summary>
    /// Redimensiona a barra de suspeita alterando o anchorMax.x do seu
    /// RectTransform (0 = vazia, 1 = cheia), em vez de usar Image.fillAmount
    /// com Image Type = Filled. A barra fica ancorada à esquerda dentro do
    /// trilho de fundo (anchorMin/Max locked em x=0), então mexer só no
    /// anchorMax.x estica/encolhe a largura de forma puramente geométrica —
    /// não depende de sprite, material ou shader nenhum, então não existe
    /// combinação de configuração que a deixe "sempre cheia" por engano.
    /// </summary>
    private void SetSuspicionFillAmount(float normalizedValue)
    {
        if (suspicionFillImage == null)
            return;

        RectTransform rect = suspicionFillImage.rectTransform;
        Vector2 anchorMax = rect.anchorMax;
        anchorMax.x = Mathf.Clamp01(normalizedValue);
        rect.anchorMax = anchorMax;
    }

    private void ApplyVisualFeedback(FeedbackVisual feedback)
    {
        // O tingimento de cor por nível de suspeita foi migrado para
        // SuspicionVisualFeedback.cs (Vignette no Global Volume), para
        // continuar funcionando mesmo com este painel fechado.
        Debug.Log($"[UIController] Feedback Visual → Animação: {feedback.animacao_trigger} | BPM: {feedback.bpm_musica}");
    }

    private void UpdateStatusText(AnalyzeResponse response = null)
    {
        if (response == null)
        {
            statusText.text = $"Sessão: {GameManager.Instance.SessionId} | Turno: 0 | Suspeita: 0%";
            return;
        }

        string lieIndicator = response.status_investigacao.detectou_mentira ? " ⚠ MENTIRA" : "";
        statusText.text = $"Turno: {response.id_turno} | " +
                         $"Suspeita: {response.status_investigacao.nivel_suspeita}%{lieIndicator}";
    }

    // ─── Scroll do texto do detetive ─────────────────────────────────────────

    private void ScrollDetectiveTextToBottom()
    {
        if (detectiveScrollRect == null) return;
        // Força o layout a recalcular antes de mover o scroll
        LayoutRebuilder.ForceRebuildLayoutImmediate(detectiveText.rectTransform);
        Canvas.ForceUpdateCanvases();
        detectiveScrollRect.verticalNormalizedPosition = 0f;
    }

    /// <summary>
    /// Envolve o detectiveText em um ScrollRect criado em runtime para que
    /// mensagens longas fiquem dentro da caixa de diálogo com scroll vertical.
    /// Hierarquia criada:
    ///   [DialogScrollView] (ScrollRect)
    ///     └─ [DialogViewport] (RectMask2D)
    ///           └─ detectiveText (ContentSizeFitter)
    /// </summary>
    private void SetupDetectiveTextScrolling()
    {
        if (detectiveText == null) return;

        detectiveText.enableWordWrapping = true;
        detectiveText.overflowMode = TextOverflowModes.Overflow;

        RectTransform textRT = detectiveText.rectTransform;
        Transform textParent = textRT.parent;
        if (textParent == null) return;

        // Não reconfigura se já existe um ScrollRect na hierarquia
        if (textRT.GetComponentInParent<ScrollRect>() != null) return;

        int sibIndex = textRT.GetSiblingIndex();

        // Guarda posicionamento original para replicar no ScrollView
        Vector2 ancMin    = textRT.anchorMin;
        Vector2 ancMax    = textRT.anchorMax;
        Vector2 offMin    = textRT.offsetMin;
        Vector2 offMax    = textRT.offsetMax;

        // ── ScrollView ────────────────────────────────────────────────────
        var scrollViewGO = new GameObject("[DialogScrollView]");
        var scrollViewRT = scrollViewGO.AddComponent<RectTransform>();
        scrollViewGO.transform.SetParent(textParent, false);
        scrollViewGO.transform.SetSiblingIndex(sibIndex);

        scrollViewRT.anchorMin = ancMin;
        scrollViewRT.anchorMax = ancMax;
        scrollViewRT.offsetMin = offMin;
        scrollViewRT.offsetMax = offMax;

        // ── Viewport (RectMask2D clippa pelo RectTransform, sem precisar de Image) ──
        var viewportGO = new GameObject("[DialogViewport]");
        var viewportRT = viewportGO.AddComponent<RectTransform>();
        viewportGO.transform.SetParent(scrollViewGO.transform, false);

        viewportRT.anchorMin = Vector2.zero;
        viewportRT.anchorMax = Vector2.one;
        viewportRT.sizeDelta = Vector2.zero;
        viewportRT.anchoredPosition = Vector2.zero;
        viewportRT.pivot = new Vector2(0f, 1f);

        viewportGO.AddComponent<RectMask2D>();

        // ── Move texto para dentro do Viewport ───────────────────────────
        textRT.SetParent(viewportGO.transform, false);
        textRT.anchorMin = new Vector2(0f, 1f);
        textRT.anchorMax = new Vector2(1f, 1f);
        textRT.pivot     = new Vector2(0.5f, 1f);
        textRT.anchoredPosition = Vector2.zero;
        textRT.sizeDelta = Vector2.zero;

        // ContentSizeFitter permite que o texto cresça verticalmente
        var csf = detectiveText.gameObject.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        // ── ScrollRect ────────────────────────────────────────────────────
        var sr = scrollViewGO.AddComponent<ScrollRect>();
        sr.content          = textRT;
        sr.viewport         = viewportRT;
        sr.horizontal       = false;
        sr.vertical         = true;
        sr.scrollSensitivity = 30f;
        sr.movementType     = ScrollRect.MovementType.Clamped;
        sr.elasticity       = 0.1f;

        detectiveScrollRect = sr;
    }

    // ─── Borda na barra de suspeita ───────────────────────────────────────────

    private void SetupSuspicionBarBorder()
    {
        if (suspicionFillImage == null) return;

        // SuspicionBar_Fill e SuspicionBar_Background são IRMÃOS dentro do HUD.
        // O pai do Fill é o próprio HUD, não o Background.
        Transform hud = suspicionFillImage.transform.parent;
        if (hud == null) return;

        if (hud.Find("[SuspicionBorder]") != null) return;

        // Encontra o Background pelo nome dentro do HUD
        Transform bgBar = hud.Find("SuspicionBar_Background");
        if (bgBar == null) return;

        var bgRT = bgBar.GetComponent<RectTransform>();
        if (bgRT == null) return;

        var borderGO = new GameObject("[SuspicionBorder]");
        var borderRT = borderGO.AddComponent<RectTransform>();
        borderGO.transform.SetParent(hud, false);

        // Mesma âncora/posição do Background, apenas 4px maior em cada eixo
        borderRT.anchorMin        = bgRT.anchorMin;
        borderRT.anchorMax        = bgRT.anchorMax;
        borderRT.pivot            = bgRT.pivot;
        borderRT.anchoredPosition = bgRT.anchoredPosition;
        borderRT.sizeDelta        = bgRT.sizeDelta + new Vector2(4f, 4f);

        var borderImg = borderGO.AddComponent<Image>();
        borderImg.color = new Color(0.85f, 0.75f, 0.15f, 1f); // dourado

        // Coloca imediatamente antes do Background (renderiza atrás)
        borderGO.transform.SetSiblingIndex(bgBar.GetSiblingIndex());
    }

    // ─── Tamanho dos elementos da HUD ────────────────────────────────────────

    private void SetupHUDSizes()
    {
        foreach (var t in FindObjectsByType<TMP_Text>(FindObjectsSortMode.None))
        {
            switch (t.gameObject.name)
            {
                case "SuspicionLabel":
                    t.fontSize = 22f;
                    break;
                case "InputPromptText":
                    t.fontSize = 22f;
                    t.enableWordWrapping = false;
                    t.overflowMode = TextOverflowModes.Overflow;
                    break;
            }
        }
    }
}
