using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD que aparece com a PRANCHETA FECHADA: suspeita, turno, alerta de 80% e as
/// dicas de teclas. Antes tudo isso morava dentro do painel da prancheta e
/// sumia junto com ela — justamente quando o jogador olha a sala.
///
/// Só mostra; nenhuma regra mora aqui. Com a prancheta aberta ele se esconde
/// (a própria prancheta já mostra status e barra), então nada aparece duas vezes.
/// Criado em código por UIController.Start, sem mexer na cena.
/// </summary>
public class PersistentHud : MonoBehaviour
{
    private const int DefaultMaxTurns = 10;
    private const string HintIdle = "[TAB] Abrir prancheta     [H] ou [F1] Ajuda     [ESC] Pausa";
    private const string HintOpen = "[TAB] Fechar prancheta     [H] ou [F1] Ajuda     [ESC] Pausa";
    private const string HintBusyOpen = "O detetive está analisando…     [TAB] Fechar prancheta     [H] ou [F1] Ajuda";
    private const string HintBusy = "O detetive está analisando…     [TAB] Abrir prancheta     [H] ou [F1] Ajuda";

    private static readonly Color Ink = new Color(0.93f, 0.90f, 0.82f);
    private static readonly Color Accent = new Color(0.85f, 0.65f, 0.25f);

    public static PersistentHud Instance { get; private set; }

    private GameObject container;
    private TMP_Text timerText;
    private TMP_Text suspicionText;
    private TMP_Text turnText;
    private TMP_Text hintText;
    private RectTransform barFill;
    private int maxTurns = DefaultMaxTurns;
    private int alert;

    public static PersistentHud Create(TMP_FontAsset font)
    {
        var go = new GameObject("[PersistentHud]");
        var hud = go.AddComponent<PersistentHud>();
        hud.Build(font);
        Instance = hud;
        return hud;
    }

    private void Start()
    {
        InterrogationUIToggle.OnMenuToggled += OnPadToggled;
        if (ApiClient.Instance != null)
        {
            ApiClient.Instance.OnResponseReceived += OnResponse;
            ApiClient.Instance.OnRequestStarted += OnBusy;
            ApiClient.Instance.OnError += OnIdle;
        }
        if (GameManager.Instance != null) GameManager.Instance.OnSuspicionChanged += OnSuspicion;
        Refresh();
    }

    private void OnDestroy()
    {
        InterrogationUIToggle.OnMenuToggled -= OnPadToggled;
        if (ApiClient.Instance != null)
        {
            ApiClient.Instance.OnResponseReceived -= OnResponse;
            ApiClient.Instance.OnRequestStarted -= OnBusy;
            ApiClient.Instance.OnError -= OnIdle;
        }
        if (GameManager.Instance != null) GameManager.Instance.OnSuspicionChanged -= OnSuspicion;
    }

    // O HUD fica SEMPRE visível (com a prancheta aberta ou fechada); o que muda
    // é a dica de teclas.
    private void OnPadToggled(bool open)
    {
        padOpen = open;
        hintText.text = busy ? (padOpen ? HintBusyOpen : HintBusy) : (padOpen ? HintOpen : HintIdle);
    }

    private bool padOpen;
    private bool busy;

    /// <summary>Contador de resposta, no canto superior direito da TELA (fora da prancheta).</summary>
    public void SetTimer(string text, bool urgent)
    {
        timerText.gameObject.SetActive(true);
        timerText.text = text;
        timerText.color = urgent ? new Color(0.9f, 0.22f, 0.18f) : Ink;
    }

    public void HideTimer() => timerText.gameObject.SetActive(false);

    private void OnBusy() { busy = true; hintText.text = padOpen ? HintBusyOpen : HintBusy; }
    private void OnIdle(string _) { busy = false; hintText.text = padOpen ? HintOpen : HintIdle; }

    private void OnResponse(AnalyzeResponse r)
    {
        busy = false;
        hintText.text = padOpen ? HintOpen : HintIdle;
        if (r.status_investigacao != null)
        {
            alert = r.status_investigacao.turnos_suspeita_alta;
            if (!r.ignorado) maxTurns = r.id_turno + r.status_investigacao.turnos_restantes;
        }
        Refresh();
    }

    // Disparado também no reinício da partida (suspeita volta a 0).
    private void OnSuspicion(int _)
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentTurn == 0)
        {
            alert = 0;
            maxTurns = DefaultMaxTurns;
        }
        Refresh();
    }

    private void Refresh()
    {
        var gm = GameManager.Instance;
        int suspicion = gm != null ? gm.SuspicionLevel : 0;
        int turn = gm != null ? gm.CurrentTurn : 0;

        suspicionText.text = $"SUSPEITA  {suspicion}%";
        suspicionText.color = suspicion >= 80 ? new Color(0.88f, 0.25f, 0.2f) : Ink;
        barFill.anchorMax = new Vector2(Mathf.Clamp01(suspicion / 100f), 1f);
        turnText.text = alert > 0 ? $"Turno {turn}/{maxTurns}   ·   Alerta 80%+: {alert}/3"
                                  : $"Turno {turn}/{maxTurns}";
    }

    // ─────────────────────────────── construção ───────────────────────────────

    private void Build(TMP_FontAsset font)
    {
        var canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 60;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        container = canvasGo;

        // Bloco superior esquerdo
        suspicionText = NewText("Suspeita", font, "SUSPEITA  0%", 34f, TextAlignmentOptions.Left);
        suspicionText.fontStyle = FontStyles.Bold;
        Place(suspicionText.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -34f), new Vector2(620f, 46f));

        var track = new GameObject("Barra", typeof(RectTransform), typeof(Image));
        track.transform.SetParent(canvasGo.transform, false);
        track.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
        Place((RectTransform)track.transform, new Vector2(0f, 1f), new Vector2(40f, -86f), new Vector2(420f, 14f));

        var fill = new GameObject("Preenchimento", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(track.transform, false);
        fill.GetComponent<Image>().color = Accent;
        barFill = (RectTransform)fill.transform;
        barFill.anchorMin = Vector2.zero; barFill.anchorMax = new Vector2(0f, 1f);
        barFill.offsetMin = Vector2.zero; barFill.offsetMax = Vector2.zero;

        turnText = NewText("Turno", font, "Turno 0/10", 26f, TextAlignmentOptions.Left);
        Place(turnText.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -108f), new Vector2(620f, 36f));

        timerText = NewText("Tempo", font, "", 52f, TextAlignmentOptions.Right);
        timerText.fontStyle = FontStyles.Bold;
        Place(timerText.rectTransform, new Vector2(1f, 1f), new Vector2(-40f, -34f), new Vector2(520f, 70f));
        timerText.gameObject.SetActive(false);

        // Dica de teclas, embaixo no centro
        hintText = NewText("Dica", font, HintIdle, 24f, TextAlignmentOptions.Center);
        hintText.color = new Color(Ink.r, Ink.g, Ink.b, 0.85f);
        Place(hintText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(1500f, 40f));
    }

    private TMP_Text NewText(string name, TMP_FontAsset font, string text, float size, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(container.transform, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = text;
        t.fontSize = size;
        t.color = Ink;
        t.alignment = align;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        // contorno escuro: legível sobre qualquer parte da sala
        t.outlineWidth = 0.22f;
        t.outlineColor = new Color32(0, 0, 0, 230);
        return t;
    }

    private static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }
}
