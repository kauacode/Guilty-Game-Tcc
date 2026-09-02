using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class ApiClient : MonoBehaviour
{
    [Header("Configuração da API")]
    [SerializeField] private string baseUrl = "http://localhost:8000";
    [SerializeField] private string apiMode = "mock"; // "mock" ou "real"
    [SerializeField] private float timeoutSeconds = 30f;

    // Singleton simples — só um ApiClient por cena
    public static ApiClient Instance { get; private set; }

    // Eventos para desacoplar o ApiClient da UI
    public event Action<AnalyzeResponse> OnResponseReceived;
    public event Action<string> OnError;
    public event Action OnRequestStarted;
    public event Action OnRequestFinished;

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

        StartCoroutine(PostToApi(sessionId, playerText));
    }

    private IEnumerator PostToApi(string sessionId, string playerText)
    {
        // Notifica que a requisição começou (para UI mostrar loading)
        OnRequestStarted?.Invoke();

        // Monta o objeto de request e serializa para JSON
        var request = new AnalyzeRequest(sessionId, playerText, apiMode);
        string jsonBody = JsonUtility.ToJson(request);

        Debug.Log($"[ApiClient] Enviando para {baseUrl}/interrogate");
        Debug.Log($"[ApiClient] Payload: {jsonBody}");

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
                string errorMsg = $"Erro HTTP {webRequest.responseCode}";
                Debug.LogError($"[ApiClient] {errorMsg}\nResposta: {webRequest.downloadHandler.text}");
                OnError?.Invoke($"{errorMsg}. Verifique o backend.");
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