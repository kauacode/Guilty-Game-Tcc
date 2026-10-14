using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Tutorial curto do interrogatório, construído em código (sem prefab nem
/// alteração de cena). Abre sozinho no início de cada partida e pode
/// ser reaberto a qualquer momento com F1.
///
/// O texto descreve SÓ o que o jogo faz hoje. As regras citadas (100%, 80% por
/// três turnos, 60 s, turno 8/12) vêm do backend (game_rules.py); se aquele
/// balanceamento mudar, este texto precisa mudar junto — por isso os números
/// ficam concentrados nas constantes abaixo.
/// </summary>
public class TutorialOverlay : MonoBehaviour
{

    // Espelham game_rules.Regras (backend).
    private const int MaxSuspicion = 100;
    private const int FirstConfession = 85;
    private const int HighSuspicion = 80;
    private const int HighSuspicionTurns = 3;
    private const int ResponseSeconds = 60;
    private const int TimeoutPenalty = 15;
    private const int MediumFromSeconds = 30;
    private const int HighFromSeconds = 45;
    private const int MediumPenalty = 1;
    private const int HighPenalty = 2;
    private const int VictoryTurn = 8;
    private const int VictoryMaxSuspicion = 30;
    private const int MaxTurns = 10;
    private const int LimitDefeatSuspicion = 60;

    private static readonly Color Ink = new Color(0.93f, 0.90f, 0.82f);
    private static readonly Color Accent = new Color(0.85f, 0.65f, 0.25f);
    private static readonly Color PanelColor = new Color(0.07f, 0.06f, 0.05f, 0.97f);

    private GameObject root;
    private CanvasGroup group;
    private ScrollRect scroll;
    private bool open;
    private CursorLockMode previousLock;
    private bool previousVisible;

    public bool IsOpen => open;
    public static TutorialOverlay Current { get; private set; }
    public static bool AnyOpen => Current != null && Current.open;

    private void OnDestroy()
    {
        if (Current == this) Current = null;
    }

    /// <summary>Cria o tutorial. <paramref name="font"/> mantém a fonte da prancheta.</summary>
    public static TutorialOverlay Create(TMP_FontAsset font)
    {
        var go = new GameObject("[TutorialOverlay]");
        var t = go.AddComponent<TutorialOverlay>();
        t.Build(font);
        Current = t;
        return t;
    }

    private void Start()
    {
        // Abre em TODA partida (a cena de jogo é recarregada a cada partida).
        // O timer só começa depois que o jogador fecha a ajuda.
        Open();
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        // F1 pode exigir a tecla Fn em notebooks; H é a alternativa (ignorada
        // enquanto o jogador digita, para não abrir a ajuda no meio do texto).
        bool typing = EventSystem.current != null &&
                      EventSystem.current.currentSelectedGameObject != null &&
                      EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>() != null;
        if (kb.f1Key.wasPressedThisFrame || (!typing && kb.hKey.wasPressedThisFrame))
        {
            if (open) Close(); else Open();
            return;
        }

        if (open && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame ||
                     kb.spaceKey.wasPressedThisFrame))
            Close();
    }

    public void Open()
    {
        if (open || root == null) return;
        open = true;
        previousLock = Cursor.lockState;
        previousVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        root.SetActive(true);
        group.alpha = 1f;
        group.blocksRaycasts = true;
        group.interactable = true;
        if (scroll != null) scroll.verticalNormalizedPosition = 1f;
    }

    public void Close()
    {
        if (!open) return;
        open = false;
        root.SetActive(false);
        Cursor.lockState = previousLock;
        Cursor.visible = previousVisible;
        // Tira o foco do botão: Enter/Espaço não podem reabrir nem enviar nada.
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    // ─────────────────────────────── construção ───────────────────────────────

    private void Build(TMP_FontAsset font)
    {
        var canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas),
                                      typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
        canvasGo.transform.SetParent(transform, false);
        root = canvasGo;
        group = canvasGo.GetComponent<CanvasGroup>();

        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;          // acima da prancheta e do pause

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // Fundo escuro que bloqueia cliques no que está por baixo.
        var dim = NewImage("Fundo", canvasGo.transform, new Color(0f, 0f, 0f, 0.82f));
        Stretch(dim.rectTransform, Vector2.zero, Vector2.one);
        dim.raycastTarget = true;

        var panel = NewImage("Painel", canvasGo.transform, PanelColor);
        var prt = panel.rectTransform;
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = new Vector2(1180f, 900f);

        var outline = panel.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(Accent.r, Accent.g, Accent.b, 0.6f);
        outline.effectDistance = new Vector2(2f, -2f);

        var title = NewText("Titulo", panel.transform, font, "COMO JOGAR", 44f, Accent, TextAlignmentOptions.Center);
        title.fontStyle = FontStyles.Bold;
        var trt = title.rectTransform;
        trt.anchorMin = new Vector2(0f, 1f); trt.anchorMax = new Vector2(1f, 1f);
        trt.pivot = new Vector2(0.5f, 1f);
        trt.anchoredPosition = new Vector2(0f, -24f);
        trt.sizeDelta = new Vector2(0f, 64f);

        // Área rolável com o texto (cabe em 1080p; rola em janelas menores).
        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(panel.transform, false);
        var vrt = viewport.GetComponent<RectTransform>();
        vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
        vrt.offsetMin = new Vector2(48f, 110f); vrt.offsetMax = new Vector2(-48f, -100f);

        var body = NewText("Texto", viewport.transform, font, BodyText(), 27f, Ink, TextAlignmentOptions.TopLeft);
        body.richText = true;
        body.textWrappingMode = TextWrappingModes.Normal;
        var brt = body.rectTransform;
        brt.anchorMin = new Vector2(0f, 1f); brt.anchorMax = new Vector2(1f, 1f);
        brt.pivot = new Vector2(0.5f, 1f);
        brt.anchoredPosition = Vector2.zero;
        brt.sizeDelta = Vector2.zero;
        var fitter = body.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll = panel.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = vrt;
        scroll.content = brt;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 35f;

        // Botão ENTENDI
        var btnImg = NewImage("BotaoEntendi", panel.transform, Accent);
        var brt2 = btnImg.rectTransform;
        brt2.anchorMin = brt2.anchorMax = brt2.pivot = new Vector2(0.5f, 0f);
        brt2.anchoredPosition = new Vector2(0f, 28f);
        brt2.sizeDelta = new Vector2(320f, 64f);
        var btn = btnImg.gameObject.AddComponent<Button>();
        btn.targetGraphic = btnImg;
        btn.onClick.AddListener(Close);
        var label = NewText("Rotulo", btnImg.transform, font, "ENTENDI", 30f,
                            new Color(0.07f, 0.06f, 0.05f), TextAlignmentOptions.Center);
        label.fontStyle = FontStyles.Bold;
        Stretch(label.rectTransform, Vector2.zero, Vector2.one);

        root.SetActive(false);
        open = false;
    }

    private static string BodyText()
    {
        string h(string s) => $"<color=#D9A640><b>{s}</b></color>";
        return
            $"Você é <b>Daniel Reis</b>, operador de impressão da Gráfica Bandeirantes e suspeito da morte de " +
            $"Otávio Lisboa. Quem conduz o interrogatório é o detetive, controlado por IA.\n\n" +

            $"{h("OBJETIVO")}\n" +
            $"Sustente o seu depoimento sem se contradizer e mantenha a <b>suspeita</b> baixa até o detetive " +
            $"não ter como acusá-lo.\n\n" +

            $"{h("COMO RESPONDER")}\n" +
            $"• <b>TAB</b> abre e fecha a prancheta. Clique em <b>RESPONDER</b>, escreva e clique em <b>ENVIAR</b>. Cada resposta enviada é um <b>turno</b>.\n" +
            $"• Ao enviar, a prancheta fecha sozinha para você ver a sala e reabre quando o detetive responder.\n" +
            $"• Textos longos rolam com a roda do mouse. Se o envio falhar, o texto fica guardado.\n\n" +

            $"{h("SUSPEITA (barra no topo)")}\n" +
            $"• Sobe quando você se contradiz, foge das perguntas ou demora. Desce com detalhes coerentes e verificáveis.\n" +
            $"• <b>Confessar</b> leva a suspeita a pelo menos {FirstConfession}%; confirmar a confissão leva a {MaxSuspicion}% (derrota).\n" +
            $"• <b>{MaxSuspicion}%</b> = derrota imediata, em qualquer turno.\n" +
            $"• <b>{HighSuspicion}% ou mais por {HighSuspicionTurns} turnos seguidos</b> = derrota. Cair abaixo de {HighSuspicion}% zera essa contagem.\n\n" +

            $"{h("TEMPO")}\n" +
            $"• Você tem <b>{ResponseSeconds} segundos</b> para responder, contados desde que a pergunta do detetive aparece (não desde o clique em RESPONDER).\n" +
            $"• Sem resposta no prazo, o detetive se irrita, o turno é perdido e a suspeita sobe <b>{TimeoutPenalty}</b> pontos.\n" +
            $"• Responder leva um pequeno custo se demorar: de {MediumFromSeconds} a {HighFromSeconds} s soma {MediumPenalty} ponto; de {HighFromSeconds} s ao fim do prazo, {HighPenalty}. O relógio é do servidor e não para com o pause.\n\n" +

            $"{h("COMO A PARTIDA TERMINA")}\n" +
            $"• <b>Vitória:</b> do turno {VictoryTurn} em diante, com suspeita de {VictoryMaxSuspicion}% ou menos, você é liberado.\n" +
            $"• O interrogatório acaba no turno <b>{MaxTurns}</b>: com {LimitDefeatSuspicion}% ou mais é derrota; abaixo disso, você é liberado.\n\n" +

            $"<color=#8C8A85>H ou F1 reabrem esta ajuda  ·  ESC pausa o jogo</color>";
    }

    // ───────────────────────────────── helpers ─────────────────────────────────

    private static Image NewImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = true;
        return img;
    }

    private static TextMeshProUGUI NewText(string name, Transform parent, TMP_FontAsset font,
                                           string text, float size, Color color, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.raycastTarget = false;
        return t;
    }

    private static void Stretch(RectTransform rt, Vector2 min, Vector2 max)
    {
        rt.anchorMin = min; rt.anchorMax = max;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }
}
