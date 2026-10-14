using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class ApiClient : MonoBehaviour
{
    [Header("Configuração da API")]
    [SerializeField] private string baseUrl = "http://localhost:8000";
    [Tooltip("90s cobre o modelo local. Medido no Llama 3.2 3B via Ollama com o " +
             "prompt real do backend: 7,8s de média e 9,7s de pico com o modelo já " +
             "carregado — mas a PRIMEIRA chamada depois de subir o Ollama passa de 16s, " +
             "porque o modelo ainda está sendo carregado na VRAM. Com os 30s antigos, " +
             "a primeira pergunta da partida corria risco de estourar antes de responder.")]
    [SerializeField] private float timeoutSeconds = 90f;

    // Singleton simples — só um ApiClient por cena
    public static ApiClient Instance { get; private set; }

    // Eventos para desacoplar o ApiClient da UI
    public event Action<AnalyzeResponse> OnResponseReceived;
    public event Action<string> OnError;
    public event Action OnRequestStarted;
    public event Action OnRequestFinished;

    /// <summary>
    /// Janela de resposta aberta (timer de 60 s). Vazia = sem regra de tempo
    /// (backend fora do ar ao clicar em RESPONDER, ou versão antiga do backend).
    /// </summary>
    public string CurrentInteractionId { get; private set; } = "";

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // sem o return, a duplicata seguia executando o resto do Awake
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Envia o depoimento do jogador para a API.
    /// Chame via: ApiClient.Instance.SendTestimony(sessionId, text);
    /// </summary>
    public void SendTestimony(string sessionId, string playerText)
    {
        if (string.IsNullOrWhiteSpace(playerText))
        {
            OnError?.Invoke("Texto vazio. Digite algo antes de enviar.");
            return;
        }

        StartCoroutine(PostToApi(sessionId, playerText, CurrentInteractionId));
    }

    /// <summary>Esquece a janela de resposta (nova partida).</summary>
    public void ResetInteraction() => CurrentInteractionId = "";

    // ─── Timer de resposta (o prazo é do SERVIDOR; aqui só se pergunta e se exibe) ───

    /// <summary>
    /// O jogador acionou RESPONDER: pede ao backend para abrir a janela de 60 s.
    /// Idempotente — se já há uma aberta, volta a mesma com o tempo que resta.
    /// </summary>
    public void OpenInteraction(string sessionId, Action<ResponseWindow> ok, Action<string> fail)
    {
        StartCoroutine(OpenInteractionRoutine(sessionId, ok, fail));
    }

    private IEnumerator OpenInteractionRoutine(string sessionId, Action<ResponseWindow> ok, Action<string> fail)
    {
        if (BackendLauncher.Instance != null)
        {
            yield return BackendLauncher.Instance.WaitUntilSettled();
            if (BackendLauncher.Instance.Status == BackendLauncher.State.Failed)
            {
                fail?.Invoke(BackendLauncher.Instance.Error);
                yield break;
            }
        }

        string url = $"{baseUrl}/session/{sessionId}/interacao";
        if (!string.IsNullOrEmpty(CaseSelection.CaseId))
            url += "?case_id=" + UnityWebRequest.EscapeURL(CaseSelection.CaseId);

        using (var req = new UnityWebRequest(url, "POST"))
        {
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = 10;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                fail?.Invoke(BackendApi.ErrorMessage(req));
                yield break;
            }

            var window = JsonUtility.FromJson<ResponseWindow>(req.downloadHandler.text);
            if (window == null || string.IsNullOrEmpty(window.interacao_id))
            {
                fail?.Invoke("Resposta inválida ao abrir a janela de resposta.");
                yield break;
            }

            CurrentInteractionId = window.interacao_id;
            ok?.Invoke(window);
        }
    }

    /// <summary>
    /// Os 60 s acabaram sem resposta. O backend confere o próprio relógio:
    /// se aplicar a penalidade, a resposta do sistema chega por
    /// OnResponseReceived como qualquer turno. Se a resposta já tinha sido
    /// enviada (corrida), `aplicado` vem falso e nada acontece.
    /// <paramref name="tooEarly"/> recebe os segundos que faltam quando o
    /// servidor acha que ainda é cedo (diferença de relógio).
    /// </summary>
    public void ExpireInteraction(string sessionId, string interactionId, Action<float> tooEarly)
    {
        StartCoroutine(ExpireInteractionRoutine(sessionId, interactionId, tooEarly));
    }

    private IEnumerator ExpireInteractionRoutine(string sessionId, string interactionId, Action<float> tooEarly)
    {
        string url = $"{baseUrl}/session/{sessionId}/interacao/{interactionId}/expirar";
        using (var req = new UnityWebRequest(url, "POST"))
        {
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = 10;
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.ProtocolError && req.responseCode == 409)
            {
                tooEarly?.Invoke(1f);
                yield break;
            }

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[ApiClient] Não foi possível expirar a interação: {BackendApi.ErrorMessage(req)}");
                yield break;
            }

            var result = JsonUtility.FromJson<ExpireResult>(req.downloadHandler.text);
            if (result == null || !result.aplicado || result.resposta == null ||
                result.resposta.status_investigacao == null || result.resposta.feedback_visual == null)
                yield break;   // já resolvida (respondida a tempo) ou inválida

            CurrentInteractionId = result.resposta.proxima_interacao_id ?? "";
            OnResponseReceived?.Invoke(result.resposta);
        }
    }

    private IEnumerator PostToApi(string sessionId, string playerText, string interactionId)
    {
        // Notifica que a requisição começou (para UI mostrar loading)
        OnRequestStarted?.Invoke();

        // O backend sobe junto com o jogo; a primeira pergunta pode chegar
        // antes de ele terminar de abrir.
        if (BackendLauncher.Instance != null)
        {
            yield return BackendLauncher.Instance.WaitUntilSettled();
            if (BackendLauncher.Instance.Status == BackendLauncher.State.Failed)
            {
                OnRequestFinished?.Invoke();
                OnError?.Invoke(BackendLauncher.Instance.Error);
                yield break;
            }
        }

        // Monta o objeto de request e serializa para JSON
        GameSettings.Load();
        var request = new AnalyzeRequest(sessionId, playerText,
                                         GameSettings.AiProvider, GameSettings.GeminiApiKey,
                                         CaseSelection.CaseId ?? "", interactionId ?? "");
        string jsonBody = JsonUtility.ToJson(request);

        // Nunca logar o payload inteiro: ele leva a chave do Gemini do jogador.
        Debug.Log($"[ApiClient] Enviando para {baseUrl}/interrogate (IA: {request.provider})");

        // Cria a requisição HTTP POST
        string url = $"{baseUrl}/interrogate";
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

        using (UnityWebRequest webRequest = new UnityWebRequest(url, "POST"))
        {
            webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            webRequest.SetRequestHeader("Content-Type", "application/json");
            webRequest.SetRequestHeader("Accept", "application/json");
            webRequest.timeout = (int)timeoutSeconds;

            // Aguarda a resposta (yield = suspende a coroutine até terminar)
            yield return webRequest.SendWebRequest();

            OnRequestFinished?.Invoke();

            // Trata erros de rede.
            // A UnityWebRequest reporta TIMEOUT como ConnectionError com error
            // "Request timeout" — não como DataProcessingError. Sem separar aqui,
            // um timeout da IA aparecia como "servidor não está rodando" e mandava
            // depurar o backend errado.
            bool timedOut = !string.IsNullOrEmpty(webRequest.error) &&
                            webRequest.error.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0;

            if (timedOut)
            {
                Debug.LogError($"[ApiClient] Timeout após {timeoutSeconds}s\nDetalhe: {webRequest.error}");
                OnError?.Invoke($"Timeout: a IA demorou mais de {timeoutSeconds:0}s para responder.");
                yield break;
            }

            if (webRequest.result == UnityWebRequest.Result.ConnectionError)
            {
                string errorMsg = "Erro de conexão. O servidor FastAPI está rodando?";
                Debug.LogError($"[ApiClient] {errorMsg}\nDetalhe: {webRequest.error}");
                OnError?.Invoke(errorMsg);
                yield break;
            }

            if (webRequest.result == UnityWebRequest.Result.ProtocolError)
            {
                // O backend manda mensagens escritas para o jogador (ex.: "O
                // modelo ainda não foi baixado. Baixe em Configurações...").
                string errorMsg = BackendApi.ErrorMessage(webRequest);
                Debug.LogError($"[ApiClient] HTTP {webRequest.responseCode}: {errorMsg}");
                OnError?.Invoke(errorMsg);
                yield break;
            }

            if (webRequest.result == UnityWebRequest.Result.DataProcessingError)
            {
                Debug.LogError($"[ApiClient] Falha ao processar o download\nDetalhe: {webRequest.error}");
                OnError?.Invoke("Resposta corrompida da API.");
                yield break;
            }

            // Sucesso — deserializa o JSON
            string responseJson = webRequest.downloadHandler.text;
            Debug.Log($"[ApiClient] Resposta recebida: {responseJson}");

            try
            {
                AnalyzeResponse response = JsonUtility.FromJson<AnalyzeResponse>(responseJson);

                if (response == null ||
                    response.status_investigacao == null ||
                    response.feedback_visual == null)
                {
                    OnError?.Invoke("Resposta inválida da API.");
                    yield break;
                }

                // Resposta que repete um turno já aplicado (ex.: o envio chegou
                // depois de o tempo ter acabado e o backend devolveu o timeout
                // de novo): não é um turno novo, então não é reaplicada.
                var gm = GameManager.Instance;
                if (gm != null && response.id_turno > 0 && response.id_turno <= gm.CurrentTurn)
                {
                    if (!gm.IsGameOver)
                        OnError?.Invoke("O tempo acabou antes de o envio ser concluído. Responda à nova pergunta.");
                    yield break;
                }

                // A próxima janela (60 s) já nasceu junto com esta fala.
                CurrentInteractionId = response.proxima_interacao_id ?? "";
                OnResponseReceived?.Invoke(response);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ApiClient] Erro ao parsear JSON: {e.Message}");
                OnError?.Invoke("Erro ao processar resposta da IA.");
            }
        }
    }
}