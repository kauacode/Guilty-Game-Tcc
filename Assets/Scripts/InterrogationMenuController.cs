using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Máquina de estados do papel amassado da prancheta.
///
/// O papel alterna entre DOIS modos, nunca os dois ao mesmo tempo:
///   • LEITURA  — mostra a fala do detetive; o botão diz "RESPONDER".
///   • ESCRITA  — mostra o campo de digitação; o botão diz "ENVIAR".
///
/// Este script cuida SÓ dessa alternância e do rótulo do botão. Quem envia o
/// depoimento, aplica regra de jogo, mexe na barra de suspeita e trata fim de
/// jogo continua sendo o UIController — 331 linhas já testadas que não faz
/// sentido reescrever.
///
/// A ponte entre os dois é o <see cref="sendProxy"/>: um Button escondido que o
/// UIController escuta como se fosse o botão de enviar. Quando o jogador clica
/// no ENVIAR visível estando em modo escrita, este script dispara o proxy e o
/// UIController faz o resto. É isso que evita o bug óbvio da abordagem direta:
/// se o UIController escutasse o botão visível, clicar em "RESPONDER" (campo
/// vazio) cairia na validação dele e sobrescreveria a fala do detetive com
/// "[Sistema] Digite algo antes de enviar."
/// </summary>
public class InterrogationMenuController : MonoBehaviour
{
    [Header("Áreas do papel (alternam entre si)")]
    [SerializeField] private GameObject detectiveArea;
    [SerializeField] private GameObject writingArea;
    [SerializeField] private TMP_InputField statementInputField;

    [Header("Botão")]
    [SerializeField] private Button submitButton;
    [SerializeField] private TMP_Text submitLabel;
    [Tooltip("Button escondido que o UIController escuta. Disparado por código " +
             "quando o envio é legítimo. Se ficar vazio, o menu funciona como " +
             "demo isolada (alterna os modos, mas não envia nada).")]
    [SerializeField] private Button sendProxy;

    private const string LabelWriting = "ENVIAR";
    private const string LabelReading = "RESPONDER";
    private const string LabelWaiting = "AGUARDANDO RESPOSTA";

    private bool isReading;

    // ─── Timer de resposta (60 s) e rascunho ───────────────────────────────
    // O prazo é do servidor: aqui só se exibe a contagem e se avisa quando ela
    // chega a zero. Nada de regra de jogo mora no cliente.
    private TMP_Text timerLabel;
    private Coroutine timerRoutine;
    private string windowId = "";
    private string lastSubmitted = "";   // o que foi para o backend na última tentativa
    private string draftToRestore = "";  // devolvido ao campo se o envio falhar

    private void Start()
    {
        if (detectiveArea == null || writingArea == null ||
            statementInputField == null || submitButton == null)
        {
            Debug.LogError("[InterrogationMenu] Referências não atribuídas — " +
                           "rode 'Guilty › Menu Interrogatório - Integrar na cena'.", this);
            enabled = false;
            return;
        }

        submitButton.onClick.AddListener(OnSubmitClicked);
        ConfigureStatementScroll();
        PlaceSubmitLabelUnderIcon();

        if (ApiClient.Instance != null)
        {
            ApiClient.Instance.OnRequestStarted  += HandleRequestStarted;
            ApiClient.Instance.OnRequestFinished += HandleRequestFinished;
            ApiClient.Instance.OnResponseReceived += HandleResponse;
            ApiClient.Instance.OnError            += HandleError;
        }

        // Abre em leitura: o UIController já escreveu a fala de abertura no
        // detectiveText durante o Start dele.
        SetReading(true);

        // A 1ª pergunta já está disponível: o prazo de 60 s começa agora — ou
        // quando o jogador fechar o tutorial, para ele não perder tempo lendo.
        StartCoroutine(StartFirstWindowWhenReady());
    }

    private IEnumerator StartFirstWindowWhenReady()
    {
        yield return null;   // dá tempo ao UIController de criar o tutorial
        while (TutorialOverlay.Current != null && TutorialOverlay.Current.IsOpen)
            yield return null;
        BeginResponseWindow();
    }

    /// <summary>Nova partida (botão Reiniciar): zera e abre a janela da 1ª pergunta.</summary>
    public void RestartTimer()
    {
        StopTimer();
        windowId = string.Empty;
        draftToRestore = string.Empty;
        lastSubmitted = string.Empty;
        BeginResponseWindow();
    }

    private void OnDestroy()
    {
        // Remove só o que este script registrou — o UIController tem os
        // próprios listeners no proxy e não pode ser desconectado junto.
        if (submitButton != null)
            submitButton.onClick.RemoveListener(OnSubmitClicked);

        if (ApiClient.Instance != null)
        {
            ApiClient.Instance.OnRequestStarted  -= HandleRequestStarted;
            ApiClient.Instance.OnRequestFinished -= HandleRequestFinished;
            ApiClient.Instance.OnResponseReceived -= HandleResponse;
            ApiClient.Instance.OnError            -= HandleError;
        }
    }

    // ─── Alternância ─────────────────────────────────────────────────────────

    private void SetReading(bool reading)
    {
        isReading = reading;

        detectiveArea.SetActive(reading);
        writingArea.SetActive(!reading);

        if (submitLabel != null)
            submitLabel.text = reading ? LabelReading : LabelWriting;

        // O timer NÃO depende do modo da prancheta: o prazo corre desde que a
        // pergunta apareceu, esteja o jogador lendo, escrevendo ou com a
        // prancheta fechada.
        if (!reading)
        {
            // Falha de envio anterior: devolve o que o jogador tinha escrito.
            statementInputField.text = draftToRestore;
            statementInputField.ActivateInputField();
        }
    }

    private void OnSubmitClicked()
    {
        if (isReading)
        {
            SetReading(false);          // "RESPONDER" → abre o campo
            return;
        }

        if (string.IsNullOrWhiteSpace(statementInputField.text))
        {
            // Campo vazio não vira turno. Não delega ao UIController porque a
            // mensagem de aviso dele iria para o detectiveText, que está oculto
            // neste modo — o jogador não veria nada e acharia que travou.
            statementInputField.ActivateInputField();
            return;
        }

        // O proxy carrega o listener do UIController, que lê o MESMO
        // InputField (ele aponta para este) e dispara a chamada à API.
        // O rascunho fica guardado até o backend confirmar: se o envio falhar,
        // o texto volta ao campo em vez de se perder.
        lastSubmitted = statementInputField.text;
        draftToRestore = lastSubmitted;
        StopTimer();   // a chegada da resposta no servidor já encerrou a contagem
        if (sendProxy != null) sendProxy.onClick.Invoke();
        else Debug.LogWarning("[InterrogationMenu] sendProxy vazio — nada foi enviado.", this);
    }

    // ─── Reações à API ───────────────────────────────────────────────────────

    private void HandleRequestStarted()
    {
        // Volta para leitura: o UIController escreve "O detetive está
        // analisando seu depoimento..." no detectiveText.
        SetReading(true);
        submitButton.interactable = false;
        // Enquanto o detetive pensa, o botão diz o que está acontecendo. O
        // rótulo volta ao normal no SetReading de HandleResponse/HandleError.
        if (submitLabel != null) submitLabel.text = LabelWaiting;
    }

    private void HandleRequestFinished()
    {
        // Espera um frame antes de reabilitar. O UIController também reage a
        // este evento e pode DESABILITAR o proxy no mesmo frame (fim de jogo).
        // Lendo o estado do proxy no frame seguinte, o botão visível herda a
        // decisão dele em vez de competir com ela.
        StartCoroutine(SyncInteractableNextFrame());
    }

    private IEnumerator SyncInteractableNextFrame()
    {
        yield return null;
        submitButton.interactable = sendProxy == null || sendProxy.interactable;
    }

    private void HandleResponse(AnalyzeResponse response)
    {
        // Resposta aceita (ou timeout): o rascunho cumpriu o papel dele.
        draftToRestore = string.Empty;
        lastSubmitted = string.Empty;
        SetReading(true);

        // Partida encerrada: sem pergunta pendente, sem contagem.
        if (response.status_investigacao != null && response.status_investigacao.fim_de_jogo)
        {
            StopTimer();
            return;
        }

        // O backend abriu a janela da próxima resposta junto com esta fala.
        if (!string.IsNullOrEmpty(response.proxima_interacao_id))
        {
            windowId = response.proxima_interacao_id;
            StartTimer(response.proxima_restante_s);
        }
        else
        {
            BeginResponseWindow();   // backend antigo: pede a janela
        }
    }

    private void HandleError(string message)
    {
        // Mantém o texto para a próxima tentativa (o backend reabre a janela
        // com o prazo inteiro quando a falha é de infraestrutura).
        draftToRestore = lastSubmitted;
        SetReading(true);
        BeginResponseWindow();   // idempotente: devolve a janela com o tempo real que resta
    }

    // ─── Janela de resposta ──────────────────────────────────────────────────

    private void BeginResponseWindow()
    {
        var api = ApiClient.Instance;
        var gm = GameManager.Instance;
        if (api == null || gm == null || gm.IsGameOver) return;

        SetTimerText("…");
        api.OpenInteraction(gm.SessionId,
            window =>
            {
                windowId = window.interacao_id;
                StartTimer(window.restante_s);
            },
            error =>
            {
                // Sem janela não há timer: o jogo segue sem a regra de tempo
                // em vez de impedir o jogador de responder.
                Debug.LogWarning($"[InterrogationMenu] Timer indisponível: {error}");
                HideTimer();
            });
    }

    private void StartTimer(float remainingSeconds)
    {
        StopTimer();
        timerRoutine = StartCoroutine(RunTimer(remainingSeconds));
    }

    private void StopTimer()
    {
        if (timerRoutine != null) { StopCoroutine(timerRoutine); timerRoutine = null; }
        HideTimer();
    }

    private IEnumerator RunTimer(float remainingSeconds)
    {
        // Tempo REAL: o pause zera o timeScale, mas o relógio do servidor segue.
        float deadline = Time.realtimeSinceStartup + remainingSeconds;
        while (true)
        {
            float left = deadline - Time.realtimeSinceStartup;
            if (left <= 0f) break;
            SetTimerText($"TEMPO  {Mathf.CeilToInt(left)}s", left <= 10f);
            yield return null;
        }

        SetTimerText("TEMPO  0s", true);
        yield return ExpireWhenServerAgrees();
    }

    private IEnumerator ExpireWhenServerAgrees()
    {
        var api = ApiClient.Instance;
        var gm = GameManager.Instance;
        if (api == null || gm == null) yield break;

        bool retry;
        int attempts = 0;
        do
        {
            retry = false;
            api.ExpireInteraction(gm.SessionId, windowId, _ => retry = true);
            yield return new WaitForSecondsRealtime(1.2f);   // dá tempo da resposta/409 chegar
            attempts++;
        } while (retry && attempts < 5);   // relógios ligeiramente diferentes: o servidor manda esperar

        timerRoutine = null;
    }

    /// <summary>
    /// As algemas ocupam o centro do botão; o rótulo ao lado dela ficava torto.
    /// Aqui ele desce para a faixa livre embaixo do desenho, centralizado.
    /// </summary>
    private void PlaceSubmitLabelUnderIcon()
    {
        if (submitLabel == null) return;
        var rt = submitLabel.rectTransform;
        rt.anchorMin = new Vector2(0.2f, 0.11f);
        rt.anchorMax = new Vector2(0.8f, 0.30f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        submitLabel.alignment = TextAlignmentOptions.Center;
        submitLabel.fontSizeMax = 26f;
    }

    private void CreateTimerLabel()
    {
        // Canvas próprio, no topo: visível independente da geometria da prancheta.
        var canvasGo = new GameObject("[ResponseTimer]", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 120;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var go = new GameObject("Tempo", typeof(RectTransform));
        go.transform.SetParent(canvasGo.transform, false);
        timerLabel = go.AddComponent<TextMeshProUGUI>();
        if (statementInputField.textComponent != null) timerLabel.font = statementInputField.textComponent.font;
        timerLabel.fontSize = 52f;
        timerLabel.fontStyle = FontStyles.Bold;
        timerLabel.alignment = TextAlignmentOptions.Right;
        timerLabel.raycastTarget = false;
        timerLabel.textWrappingMode = TextWrappingModes.NoWrap;
        timerLabel.outlineWidth = 0.25f;
        timerLabel.outlineColor = new Color32(0, 0, 0, 235);
        // Canto superior direito: fora da prancheta, sempre visível.
        var rt = timerLabel.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-40f, -34f);
        rt.sizeDelta = new Vector2(520f, 70f);
        go.SetActive(false);
    }

    // O contador mora no HUD da tela (PersistentHud), fora da hierarquia da
    // prancheta: um Canvas filho da prancheta herdava a posição dela e o
    // contador aparecia no meio do papel.
    private void SetTimerText(string text, bool urgent = false)
        => PersistentHud.Instance?.SetTimer(text, urgent);

    private void HideTimer() => PersistentHud.Instance?.HideTimer();

    // ─── Campo de texto longo ────────────────────────────────────────────────

    /// <summary>
    /// Texto digitado maior que o papel rola em vez de vazar: quebra de linha
    /// (inclusive de palavras muito longas), máscara pelo viewport e barra de
    /// rolagem fina. A roda do mouse e o cursor também rolam o conteúdo.
    /// </summary>
    private void ConfigureStatementScroll()
    {
        var text = statementInputField.textComponent;
        if (text != null)
        {
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
        }
        if (statementInputField.placeholder is TMP_Text ph)
        {
            ph.textWrappingMode = TextWrappingModes.Normal;
            ph.overflowMode = TextOverflowModes.Overflow;
        }

        statementInputField.lineType = TMP_InputField.LineType.MultiLineNewline;
        statementInputField.scrollSensitivity = 25f;

        if (statementInputField.verticalScrollbar != null) return;

        var track = new Color(0f, 0f, 0f, 0.08f);
        var ink = new Color(0.16f, 0.13f, 0.10f, 0.55f);

        var barGo = new GameObject("[StatementScrollbar]", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
        barGo.transform.SetParent(statementInputField.transform, false);
        var barRt = barGo.GetComponent<RectTransform>();
        barRt.anchorMin = new Vector2(1f, 0f);
        barRt.anchorMax = new Vector2(1f, 1f);
        barRt.pivot = new Vector2(1f, 0.5f);
        barRt.sizeDelta = new Vector2(8f, 0f);
        barRt.anchoredPosition = Vector2.zero;
        barGo.GetComponent<Image>().color = track;

        var areaGo = new GameObject("Sliding Area", typeof(RectTransform));
        areaGo.transform.SetParent(barGo.transform, false);
        var areaRt = areaGo.GetComponent<RectTransform>();
        areaRt.anchorMin = Vector2.zero; areaRt.anchorMax = Vector2.one;
        areaRt.offsetMin = Vector2.zero; areaRt.offsetMax = Vector2.zero;

        var handleGo = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handleGo.transform.SetParent(areaGo.transform, false);
        var handleRt = handleGo.GetComponent<RectTransform>();
        handleRt.offsetMin = Vector2.zero; handleRt.offsetMax = Vector2.zero;
        var handleImg = handleGo.GetComponent<Image>();
        handleImg.color = ink;

        var bar = barGo.GetComponent<Scrollbar>();
        bar.handleRect = handleRt;
        bar.targetGraphic = handleImg;
        bar.direction = Scrollbar.Direction.BottomToTop;

        // Reserva a faixa da barra para o texto não passar por baixo dela.
        if (statementInputField.textViewport != null)
        {
            var vp = statementInputField.textViewport;
            vp.offsetMax = new Vector2(-12f, vp.offsetMax.y);
        }
        statementInputField.verticalScrollbar = bar;
    }

    // ─── API pública (para outros sistemas, ex.: timeout de turno) ───────────

    /// <summary>Força o papel de volta ao modo de escrita.</summary>
    public void OpenWriting() => SetReading(false);

    /// <summary>Força o papel de volta ao modo de leitura.</summary>
    public void OpenReading() => SetReading(true);
}
