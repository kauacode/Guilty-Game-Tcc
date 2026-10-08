using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Página "Detetive (IA)" das Configurações: escolha da IA, chave do Gemini e
/// download do modelo local.
///
/// Mesma divisão do SettingsPanel: a UI é montada pelo editor
/// (Guilty > UI - Página Detetive (IA)) e aqui mora só o comportamento. A
/// lista de IAs vem do backend (/providers) — acrescentar um modelo novo no
/// catálogo do backend faz ele aparecer aqui sem mexer no Unity.
/// </summary>
public class AiSettingsPage : MonoBehaviour
{
    [Header("Escolha")]
    [SerializeField] private TMP_Dropdown providerDropdown;
    [SerializeField] private TMP_Text descriptionText;

    [Header("Gemini")]
    [SerializeField] private GameObject keyRow;
    [SerializeField] private TMP_InputField keyInput;
    [SerializeField] private Button testKeyButton;

    [Header("Modelo local")]
    [SerializeField] private GameObject modelRow;
    [SerializeField] private Button modelButton;
    [SerializeField] private TMP_Text modelButtonLabel;
    [SerializeField] private GameObject progressBar;
    [SerializeField] private RectTransform progressFill;

    [Header("Estado")]
    [SerializeField] private TMP_Text statusText;

    private const string GetKeyUrl = "https://aistudio.google.com/apikey";

    // Mesmas cores do GuiltyNoirUI, que só existe no editor.
    private static readonly Color Amber     = new Color(0.78f, 0.53f, 0.16f, 1f);
    private static readonly Color TextMuted = new Color(0.62f, 0.61f, 0.58f, 1f);
    private static readonly Color Danger    = new Color(0.85f, 0.32f, 0.26f, 1f);
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private BackendApi.ProviderInfo[] providers;
    private bool wiring;
    private bool confirmingDelete;
    private bool busy;
    private Coroutine polling;

    private BackendApi.ProviderInfo Selected =>
        providers != null && providerDropdown.value >= 0 && providerDropdown.value < providers.Length
            ? providers[providerDropdown.value] : null;

    private void OnEnable() => StartCoroutine(Refresh());

    private void OnDisable()
    {
        // O download continua no backend; ao reabrir a página o progresso volta.
        StopAllCoroutines();
        polling = null;
        busy = false;
        confirmingDelete = false;
    }

    // ─────────────────────────────── carga ───────────────────────────────

    private IEnumerator Refresh()
    {
        providerDropdown.interactable = false;
        keyRow.SetActive(false);
        modelRow.SetActive(false);
        progressBar.SetActive(false);
        descriptionText.text = "";
        SetStatus("Conectando ao detetive...", TextMuted);

        BackendApi.ProviderInfo[] list = null;
        string error = null;
        yield return BackendApi.GetProviders(l => list = l, e => error = e);
        if (error != null || list == null || list.Length == 0)
        {
            SetStatus(error ?? "O backend não informou nenhuma IA.", Danger);
            yield break;
        }

        providers = list;
        GameSettings.Load();

        wiring = true;
        providerDropdown.ClearOptions();
        providerDropdown.AddOptions(list.Select(p => p.nome).ToList());
        int index = Array.FindIndex(list, p => p.id == GameSettings.AiProvider);
        providerDropdown.value = Mathf.Max(0, index);
        providerDropdown.RefreshShownValue();
        keyInput.text = GameSettings.GeminiApiKey;
        wiring = false;

        providerDropdown.interactable = true;
        GameSettings.SetAiProvider(Selected.id);
        UpdateView();
    }

    // ───────────────────────── callbacks (Inspector) ─────────────────────────

    public void OnProviderChanged(int index)
    {
        if (wiring || Selected == null) return;
        GameSettings.SetAiProvider(Selected.id);
        confirmingDelete = false;
        UpdateView();
    }

    public void OnKeyEdited(string value)
    {
        if (wiring) return;
        GameSettings.SetGeminiApiKey(value);
        UpdateView();
    }

    public void OnGetKey() => Application.OpenURL(GetKeyUrl);

    public void OnTestKey()
    {
        if (!busy) StartCoroutine(TestKey());
    }

    public void OnModelButton()
    {
        var p = Selected;
        if (p == null || busy) return;

        switch (p.download?.estado)
        {
            case "baixando":
                StartCoroutine(Call(BackendApi.CancelDownload, p));
                break;
            case "verificando":
                break;   // falta pouco; cancelar aqui só jogaria fora o arquivo inteiro
            case "concluido":
                if (!confirmingDelete)
                {
                    // Apagar custa 2 GB de download para desfazer: pede um 2º clique.
                    confirmingDelete = true;
                    UpdateView();
                }
                else
                {
                    confirmingDelete = false;
                    StartCoroutine(Call(BackendApi.DeleteModel, p));
                }
                break;
            default:
                StartCoroutine(Call(BackendApi.StartDownload, p));
                break;
        }
    }

    // ─────────────────────────────── ações ───────────────────────────────

    private IEnumerator TestKey()
    {
        string key = keyInput.text.Trim();
        GameSettings.SetGeminiApiKey(key);
        if (key.Length == 0 && !(Selected?.chave_no_servidor ?? false))
        {
            SetStatus("Cole sua chave antes de testar.", Danger);
            yield break;
        }

        busy = true;
        testKeyButton.interactable = false;
        SetStatus("Testando a chave com o Google...", TextMuted);

        BackendApi.OkResponse result = null;
        string error = null;
        yield return BackendApi.TestGeminiKey(key, r => result = r, e => error = e);

        busy = false;
        testKeyButton.interactable = true;
        if (error != null) SetStatus(error, Danger);
        else if (result.ok) SetStatus("Chave válida. O Gemini está pronto para interrogar.", Amber);
        else SetStatus(result.erro, Danger);
    }

    private IEnumerator Call(Func<string, Action<BackendApi.DownloadStatus>, Action<string>, IEnumerator> action,
                             BackendApi.ProviderInfo p)
    {
        busy = true;
        modelButton.interactable = false;

        string error = null;
        yield return action(p.id, s => ApplyDownload(p, s), e => error = e);

        busy = false;
        modelButton.interactable = true;
        if (error != null) { SetStatus(error, Danger); yield break; }

        UpdateView();
        if (IsActive(p.download) && polling == null) polling = StartCoroutine(Poll(p));
    }

    private IEnumerator Poll(BackendApi.ProviderInfo p)
    {
        while (IsActive(p.download))
        {
            yield return new WaitForSecondsRealtime(0.5f);
            string error = null;
            yield return BackendApi.GetDownload(p.id, s => ApplyDownload(p, s), e => error = e);
            if (error != null) { SetStatus(error, Danger); break; }
            if (Selected == p) UpdateView();
        }
        polling = null;
    }

    private static void ApplyDownload(BackendApi.ProviderInfo p, BackendApi.DownloadStatus s)
    {
        p.download = s;
        p.baixado = s.estado == "concluido";
    }

    private static bool IsActive(BackendApi.DownloadStatus d) =>
        d != null && (d.estado == "baixando" || d.estado == "verificando");

    // ─────────────────────────────── visual ───────────────────────────────

    private void UpdateView()
    {
        var p = Selected;
        if (p == null) return;

        bool cloud = p.requer_chave;
        keyRow.SetActive(cloud);
        modelRow.SetActive(!cloud);
        progressBar.SetActive(!cloud && IsActive(p.download));

        if (cloud)
        {
            descriptionText.text = p.descricao;
            if (GameSettings.GeminiApiKey.Length > 0)
                SetStatus("Chave salva. Use TESTAR para conferir.", TextMuted);
            else if (p.chave_no_servidor)
                SetStatus("Sem chave própria: usando a chave do servidor de desenvolvimento.", TextMuted);
            else
                SetStatus("Cole sua chave gratuita do Google AI Studio. Não tem? Clique em OBTER.", Amber);
            return;
        }

        descriptionText.text = $"{p.descricao}\nDownload de {Gb(p.tamanho_bytes)} · requer {p.ram_minima_gb} GB de RAM.";
        UpdateModelView(p);

        if (IsActive(p.download) && polling == null) polling = StartCoroutine(Poll(p));
    }

    private void UpdateModelView(BackendApi.ProviderInfo p)
    {
        var d = p.download ?? new BackendApi.DownloadStatus { estado = p.baixado ? "concluido" : "parado" };
        modelButton.interactable = !busy && d.estado != "verificando";

        switch (d.estado)
        {
            case "concluido":
                SetButton(confirmingDelete ? "Confirmar: apagar" : "Apagar modelo");
                SetStatus(confirmingDelete
                    ? $"Clique de novo para apagar e liberar {Gb(p.tamanho_bytes)} do disco."
                    : "Modelo baixado e pronto. O detetive vai rodar no seu computador, sem internet.",
                    confirmingDelete ? Danger : Amber);
                break;

            case "baixando":
                SetButton("Cancelar");
                SetProgress(d);
                SetStatus($"Baixando... {Percent(d)} ({Gb(d.baixados)} de {Gb(d.total)})", TextMuted);
                break;

            case "verificando":
                SetButton("Conferindo...");
                SetProgress(d);
                SetStatus("Download completo. Conferindo a integridade do arquivo...", TextMuted);
                break;

            case "erro":
                SetButton(d.baixados > 0 ? "Continuar" : "Tentar de novo");
                SetStatus(d.erro, Danger);
                break;

            default: // parado | cancelado
                bool partial = d.baixados > 0;
                SetButton(partial ? "Continuar" : $"Baixar ({Gb(p.tamanho_bytes)})");
                SetStatus(partial
                    ? $"Download pausado em {Percent(d)}. Continue de onde parou."
                    : "Ainda não baixado. Baixe uma vez e jogue offline.", TextMuted);
                break;
        }
    }

    private void SetProgress(BackendApi.DownloadStatus d)
    {
        float k = d.total > 0 ? Mathf.Clamp01((float)d.baixados / d.total) : 0f;
        progressFill.anchorMax = new Vector2(k, 1f);
    }

    private void SetButton(string label) => modelButtonLabel.text = label.ToUpperInvariant();

    private void SetStatus(string text, Color color)
    {
        statusText.text = text;
        statusText.color = color;
    }

    private static string Percent(BackendApi.DownloadStatus d) =>
        d.total > 0 ? $"{100.0 * d.baixados / d.total:0}%" : "0%";

    private static string Gb(long bytes) => string.Format(PtBr, "{0:0.0} GB", bytes / 1e9);
}
