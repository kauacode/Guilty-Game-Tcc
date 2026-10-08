using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Chamadas ao backend fora do interrogatório: lista de IAs, teste da chave
/// do Gemini e download dos modelos locais. Usado pela página Detetive (IA)
/// das Configurações.
///
/// O interrogatório em si continua no ApiClient — este arquivo não toca nele.
/// </summary>
public static class BackendApi
{
    [Serializable]
    public class DownloadStatus
    {
        public string estado;   // parado | baixando | verificando | concluido | erro | cancelado
        public long baixados;
        public long total;
        public string erro;
    }

    [Serializable]
    public class ProviderInfo
    {
        public string id;
        public string nome;
        public string tipo;     // "nuvem" | "local"
        public string descricao;
        public bool requer_chave;
        public bool chave_no_servidor;
        public bool baixado;
        public long tamanho_bytes;
        public int ram_minima_gb;
        public DownloadStatus download;
    }

    [Serializable] private class ProvidersResponse { public ProviderInfo[] providers; }
    [Serializable] private class KeyTestRequest { public string gemini_api_key; }
    [Serializable] public class OkResponse { public bool ok; public string erro; }
    [Serializable] private class ErrorBody { public string detail; }

    public static IEnumerator GetProviders(Action<ProviderInfo[]> ok, Action<string> fail)
    {
        yield return Send("GET", "/providers", null,
            json => ok(JsonUtility.FromJson<ProvidersResponse>(json).providers), fail);
    }

    public static IEnumerator TestGeminiKey(string key, Action<OkResponse> ok, Action<string> fail)
    {
        string body = JsonUtility.ToJson(new KeyTestRequest { gemini_api_key = key });
        yield return Send("POST", "/providers/gemini/testar", body,
            json => ok(JsonUtility.FromJson<OkResponse>(json)), fail);
    }

    /// <summary>
    /// Pede ao backend para carregar o modelo e processar a parte fixa do
    /// prompt enquanto o jogador ainda olha a cena. Responde na hora; com o
    /// Qwen a 1ª pergunta cai de ~37s para ~17s. No Gemini não faz nada.
    /// </summary>
    public static IEnumerator WarmUp(string providerId, Action<OkResponse> ok, Action<string> fail)
    {
        yield return Send("POST", $"/providers/{providerId}/aquecer", null,
            json => ok(JsonUtility.FromJson<OkResponse>(json)), fail);
    }

    /// <summary>
    /// Finaliza a partida no backend: apaga o histórico e o estado dela.
    /// Cada partida é única — a próxima nunca continua esta.
    /// </summary>
    public static IEnumerator EndSession(string sessionId)
    {
        yield return Send("DELETE", $"/session/{sessionId}", null, _ => { },
            e => Debug.LogWarning($"[BackendApi] Não foi possível finalizar a sessão {sessionId}: {e}"));
    }

    public static IEnumerator StartDownload(string modelId, Action<DownloadStatus> ok, Action<string> fail)
        => Send("POST", $"/models/{modelId}/download", null, json => ok(Parse(json)), fail);

    public static IEnumerator GetDownload(string modelId, Action<DownloadStatus> ok, Action<string> fail)
        => Send("GET", $"/models/{modelId}/download", null, json => ok(Parse(json)), fail);

    public static IEnumerator CancelDownload(string modelId, Action<DownloadStatus> ok, Action<string> fail)
        => Send("DELETE", $"/models/{modelId}/download", null, json => ok(Parse(json)), fail);

    public static IEnumerator DeleteModel(string modelId, Action<DownloadStatus> ok, Action<string> fail)
        => Send("DELETE", $"/models/{modelId}", null, json => ok(Parse(json)), fail);

    private static DownloadStatus Parse(string json) => JsonUtility.FromJson<DownloadStatus>(json);

    /// <summary>
    /// Mensagem do backend ({"detail": "..."}) quando houver; senão, genérica.
    /// O backend escreve as mensagens já pensando no jogador.
    /// </summary>
    public static string ErrorMessage(UnityWebRequest req)
    {
        try
        {
            var body = JsonUtility.FromJson<ErrorBody>(req.downloadHandler?.text ?? "");
            if (body != null && !string.IsNullOrEmpty(body.detail)) return body.detail;
        }
        catch { /* corpo não era JSON (ou detail era lista, como no 422) */ }

        return req.result == UnityWebRequest.Result.ConnectionError
            ? "Sem conexão com o detetive (backend)."
            : $"Erro HTTP {req.responseCode}.";
    }

    private static IEnumerator Send(string method, string path, string jsonBody,
                                    Action<string> ok, Action<string> fail)
    {
        if (BackendLauncher.Instance != null)
        {
            yield return BackendLauncher.Instance.WaitUntilSettled();
            if (BackendLauncher.Instance.Status == BackendLauncher.State.Failed)
            {
                fail(BackendLauncher.Instance.Error);
                yield break;
            }
        }

        using (var req = new UnityWebRequest(BackendLauncher.BaseUrl + path, method))
        {
            if (jsonBody != null)
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
                req.SetRequestHeader("Content-Type", "application/json");
            }
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = 30;

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                fail(ErrorMessage(req));
                yield break;
            }

            try { ok(req.downloadHandler.text); }
            catch (Exception e) { fail($"Resposta inesperada do backend: {e.Message}"); }
        }
    }
}
