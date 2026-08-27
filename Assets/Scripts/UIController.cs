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

    [Header("Elementos de Output")]
    [SerializeField] private TMP_Text detectiveText;
    [SerializeField] private TMP_Text statusText;

    [Header("HUD — Nível de Suspeita")]
    [Tooltip("Barra fina no topo da tela. Preenchida via RectTransform.anchorMax.x (0=vazia, 1=cheia) em vez de Image.fillAmount — evita depender de sprite/Image Type=Filled, que exige um sprite válido para funcionar.")]
    [SerializeField] private Image suspicionFillImage;

    [Header("Feedback de Loading")]
    [SerializeField] private GameObject loadingIndicator;
    [SerializeField] private CanvasGroup canvasGroup; // para bloquear UI

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

        // Estado inicial da UI
        SetLoadingState(false);
        detectiveText.text = "Detetive Marcos Silva está esperando seu depoimento.\n\n" +
                             "Onde você estava na noite do dia 14 de março?";
        UpdateStatusText();

        SetSuspicionFillAmount(0f);
    }

    private void OnDestroy()
    {
        // Sempre remova listeners para evitar memory leaks
        if (ApiClient.Instance != null)
        {
            ApiClient.Instance.OnResponseReceived -= HandleApiResponse;
            ApiClient.Instance.OnError -= HandleApiError;
            ApiClient.Instance.OnRequestStarted -= HandleRequestStarted;
            ApiClient.Instance.OnRequestFinished -= HandleRequestFinished;
        }
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
            sendButton.interactable = false;
        }
    }

    private void HandleApiError(string errorMessage)
    {
        detectiveText.text = $"<color=#FF4444>[Erro] {errorMessage}</color>\n\nVerifique se o servidor está rodando.";
        Debug.LogError($"[UIController] Erro da API: {errorMessage}");
    }

    private void HandleRequestStarted()
    {
        SetLoadingState(true);
        detectiveText.text = "O detetive está analisando seu depoimento...";
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
}
