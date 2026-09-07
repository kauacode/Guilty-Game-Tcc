using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Monta a skin "prancheta" do menu de interrogatório.
///
/// Dois modos, no menu Guilty:
///   • "Menu Interrogatório - Integrar na cena"  → o que se usa de verdade.
///     Monta a prancheta DENTRO do Canvas que já existe na cena de jogo e
///     repõe as referências do UIController e do InterrogationUIToggle nela.
///     O painel antigo é desativado (não apagado) e renomeado com [ANTIGO].
///   • "Menu Interrogatório - Construir avulso" → Canvas isolado + prefab,
///     para inspecionar o layout sem mexer na cena de jogo.
///
/// Hierarquia da prancheta:
///   ClipboardPanel              Image (prancheta) + CanvasGroup
///     ├ StatusPaper             Image (papel envelhecido)
///     │   ├ StatusHeader        TMP "Status"
///     │   └ StatusText          TMP  ← escrito pelo UIController
///     ├ StatementPaper          Image (papel amassado)
///     │   ├ DetectiveArea       ativo no modo LEITURA
///     │   │   └ DetectiveText   TMP  ← escrito pelo UIController
///     │   └ WritingArea         ativo no modo ESCRITA
///     │       └ StatementInputField  TMP_InputField sem fundo próprio
///     ├ SubmitButton            Button visível (ENVIAR / RESPONDER)
///     │   └ SubmitLabel         TMP
///     └ RestartButton           Button, oculto até o fim de jogo
///         └ RestartLabel        TMP
/// </summary>
public static class InterrogationMenuBuilder
{
    // ─── Assets esperados ────────────────────────────────────────────────────
    private const string SpriteFolder    = "Assets/UI/Interrogation";
    private const string ClipboardSprite = SpriteFolder + "/UI_Clipboard.png";
    private const string StatusSprite    = SpriteFolder + "/UI_PaperAged.png";
    private const string StatementSprite = SpriteFolder + "/UI_PaperCrumpled.png";
    private const string ButtonSprite    = SpriteFolder + "/UI_ButtonRed.png";

    private const string PrefabFolder = "Assets/Prefabs/UI";
    private const string PrefabPath   = PrefabFolder + "/PF_InterrogationMenu.prefab";

    private const string GameScenePath = "Assets/Scenes/SampleScene.unity";
    private const string RootName      = "ClipboardPanel";
    private const string CanvasName    = "InterrogationCanvas";

    // ─── Layout ──────────────────────────────────────────────────────────────
    private const float ClipboardHeight        = 940f;
    private const float ClipboardFallbackAspect = 0.848f;

    // Cada peça: fração da LARGURA da prancheta + distância do topo. A ALTURA
    // sai da proporção real do sprite (PlaceByAspect) — fixá-la à mão faz a
    // arte entrar esticada, e esticar papel de borda rasgada denuncia a montagem.
    // O topo começa em 0.115 porque essa faixa é o prendedor de metal.
    // Valores calculados com as proporções REAIS da arte já tratada
    // (prancheta 0.848, status 2.029, papel 1.675, botão 2.397). Resultado:
    // margem de 8.5% no topo (abaixo do prendedor), ~2.2% entre as peças e
    // ~1.3% na base. Mexer numa fração de largura muda a altura junto, então
    // confira as sobras se ajustar — o builder avisa se algo passar da base.
    private const float StatusWidthFrac    = 0.55f, StatusTopFrac    = 0.085f;
    private const float StatementWidthFrac = 0.78f, StatementTopFrac = 0.337f;
    // O botão é largo (66%) por um motivo: as algemas ocupam o miolo do sprite
    // (~0.36–0.64), e o rótulo tem de caber na faixa à direita delas. Estreitar
    // o botão espreme "RESPONDER" até ficar ilegível.
    private const float SubmitWidthFrac    = 0.66f, SubmitTopFrac    = 0.754f;

    private const float StatusFallbackAspect    = 2.029f;
    private const float StatementFallbackAspect = 1.675f;
    private const float SubmitFallbackAspect    = 2.397f;

    // ─── Paleta ──────────────────────────────────────────────────────────────
    // Tinta de máquina de escrever: chumbo levemente quente. Preto puro sobre
    // papel envelhecido lê como texto digital e quebra a ilusão.
    private static readonly Color InkDark    = new Color(0.16f, 0.15f, 0.14f, 1f);
    private static readonly Color InkMuted   = new Color(0.34f, 0.31f, 0.27f, 1f);
    private static readonly Color InkFaded   = new Color(0.16f, 0.15f, 0.14f, 0.45f);
    private static readonly Color LabelOnRed = new Color(0.93f, 0.92f, 0.90f, 1f);

    private static readonly Color FallbackClipboard = new Color(0.22f, 0.23f, 0.22f, 1f);
    private static readonly Color FallbackPaperAged = new Color(0.86f, 0.80f, 0.65f, 1f);
    private static readonly Color FallbackPaperGrey = new Color(0.90f, 0.89f, 0.85f, 1f);
    private static readonly Color FallbackButtonRed = new Color(0.72f, 0.16f, 0.13f, 1f);

    private const string PlaceholderText = "Digite seu depoimento aqui...";

    /// <summary>Referências produzidas pela montagem, para religar os controllers.</summary>
    private class Refs
    {
        public GameObject Root;
        public CanvasGroup Group;
        public GameObject DetectiveArea, WritingArea;
        public TMP_Text DetectiveText, StatusText, SubmitLabel;
        public TMP_InputField Input;
        public Button Submit, Restart;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Modo 1 — integrar na cena de jogo
    // ═══════════════════════════════════════════════════════════════════════

    [MenuItem("Guilty/Menu Interrogatório - Integrar na cena")]
    public static void Integrate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[InterrogMenu] Saia do Play mode antes de integrar.");
            return;
        }

        var scene = SceneManager.GetActiveScene();
        if (scene.path != GameScenePath)
        {
            if (!EditorUtility.DisplayDialog("Abrir a cena de jogo?",
                    $"A prancheta precisa ser montada em {GameScenePath}.\n\n" +
                    "Alterações não salvas na cena atual serão perguntadas antes.",
                    "Abrir SampleScene", "Cancelar"))
                return;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
        }

        var ui = Object.FindFirstObjectByType<UIController>(FindObjectsInactive.Include);
        if (ui == null)
        {
            Debug.LogError("[InterrogMenu] Nenhum UIController na cena. " +
                           "A integração precisa dele — ele é quem fala com a API.");
            return;
        }

        GameObject canvasGo = ui.gameObject;
        var toggle = canvasGo.GetComponent<InterrogationUIToggle>();

        int missing = 0;
        var sprites = LoadSprites(ref missing);

        RemoveExisting(canvasGo.transform);
        GameObject oldPanel = RetireOldPanel(toggle, canvasGo);

        var refs = BuildClipboard(canvasGo.transform, sprites);

        // Proxy: Button escondido que o UIController escuta como "enviar".
        // Ver o comentário de classe do InterrogationMenuController para o porquê.
        var proxy = MakeSendProxy(canvasGo.transform);

        WireUIController(ui, refs, proxy);
        WireToggle(toggle, refs, canvasGo);
        WireMenuController(canvasGo, refs, proxy);

        EnsureEventSystem();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Selection.activeGameObject = refs.Root;

        Debug.Log($"[InterrogMenu] Prancheta integrada em {GameScenePath} e a cena foi salva.\n" +
                  $"Painel antigo preservado (desativado) como '{(oldPanel != null ? oldPanel.name : "—")}'.",
                  refs.Root);
        ReportSpriteStatus(missing);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Modo 2 — canvas avulso + prefab (só para inspecionar o layout)
    // ═══════════════════════════════════════════════════════════════════════

    [MenuItem("Guilty/Menu Interrogatório - Construir avulso")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[InterrogMenu] Saia do Play mode antes de construir.");
            return;
        }

        int missing = 0;
        var sprites = LoadSprites(ref missing);

        var old = GameObject.Find(CanvasName);
        if (old != null) Undo.DestroyObjectImmediate(old);

        var canvasGo = new GameObject(CanvasName,
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Undo.RegisterCreatedObjectUndo(canvasGo, "Build Interrogation Menu");
        canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        ConfigureScaler(canvasGo.GetComponent<CanvasScaler>());

        var refs = BuildClipboard(canvasGo.transform, sprites);
        WireMenuController(canvasGo, refs, null);
        EnsureEventSystem();

        Directory.CreateDirectory(PrefabFolder);
        var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(
            canvasGo, PrefabPath, InteractionMode.UserAction);
        AssetDatabase.Refresh();

        Selection.activeGameObject = canvasGo;
        Debug.Log($"[InterrogMenu] Canvas avulso montado e prefab salvo em {PrefabPath}. " +
                  "Este modo NÃO liga nada ao jogo — use 'Integrar na cena' para jogar.", prefab);
        ReportSpriteStatus(missing);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Montagem
    // ═══════════════════════════════════════════════════════════════════════

    private static Refs BuildClipboard(Transform parent, Sprite[] s)
    {
        Sprite clipboard = s[0], statusPap = s[1], statement = s[2], buttonRed = s[3];
        var refs = new Refs();

        // ── prancheta ────────────────────────────────────────────────────────
        var root = MakeImage(RootName, parent, clipboard, FallbackClipboard);
        var rootRt = root.GetComponent<RectTransform>();
        rootRt.anchorMin = rootRt.anchorMax = rootRt.pivot = new Vector2(0.5f, 0.5f);
        rootRt.anchoredPosition = Vector2.zero;
        Vector2 size = ResolveClipboardSize(clipboard);
        rootRt.sizeDelta = size;

        // Alvo de raycast: sem isso, cliques no metal vazam para a cena 3D atrás.
        root.GetComponent<Image>().raycastTarget = true;

        refs.Root  = root;
        refs.Group = root.AddComponent<CanvasGroup>();

        // ── papel de status ──────────────────────────────────────────────────
        var statusGo = MakeImage("StatusPaper", root.transform, statusPap, FallbackPaperAged);
        PlaceByAspect(statusGo.GetComponent<RectTransform>(), statusPap,
                      StatusFallbackAspect, StatusWidthFrac, StatusTopFrac, size);
        statusGo.GetComponent<Image>().raycastTarget = false;

        var header = MakeText("StatusHeader", statusGo.transform, "Status",
                              24f, InkMuted, TextAlignmentOptions.TopLeft);
        header.fontStyle = FontStyles.SmallCaps;
        Stretch(header.rectTransform, new Vector2(0.09f, 0.60f), new Vector2(0.92f, 0.86f));

        // UM texto só, e não três: o UIController já produz a linha inteira
        // ("Sessão: x | Turno: n | Suspeita: n%") em UpdateStatusText(). Três
        // campos separados exigiriam reescrever aquele método e passariam a ter
        // duas fontes da verdade para o mesmo dado.
        refs.StatusText = MakeText("StatusText", statusGo.transform, "Sessão: — | Turno: 0 | Suspeita: 0%",
                                   26f, InkDark, TextAlignmentOptions.TopLeft);
        Stretch(refs.StatusText.rectTransform, new Vector2(0.09f, 0.16f), new Vector2(0.92f, 0.60f));

        // ── papel de depoimento ──────────────────────────────────────────────
        var paperGo = MakeImage("StatementPaper", root.transform, statement, FallbackPaperGrey);
        PlaceByAspect(paperGo.GetComponent<RectTransform>(), statement,
                      StatementFallbackAspect, StatementWidthFrac, StatementTopFrac, size);
        paperGo.GetComponent<Image>().raycastTarget = false;

        // As duas áreas ocupam o MESMO retângulo e se alternam.
        refs.DetectiveArea = MakeArea("DetectiveArea", paperGo.transform);
        refs.WritingArea   = MakeArea("WritingArea",   paperGo.transform);

        refs.DetectiveText = MakeText("DetectiveText", refs.DetectiveArea.transform, string.Empty,
                                      27f, InkDark, TextAlignmentOptions.TopLeft);
        Stretch(refs.DetectiveText.rectTransform, Vector2.zero, Vector2.one);

        refs.Input = BuildInputField(refs.WritingArea.transform);

        // ── botão de envio ───────────────────────────────────────────────────
        var submitGo = MakeImage("SubmitButton", root.transform, buttonRed, FallbackButtonRed);
        PlaceByAspect(submitGo.GetComponent<RectTransform>(), buttonRed,
                      SubmitFallbackAspect, SubmitWidthFrac, SubmitTopFrac, size);
        refs.Submit      = MakeButton(submitGo);
        refs.SubmitLabel = MakeButtonLabel(submitGo, "SubmitLabel", "ENVIAR");

        // ── botão de reiniciar (oculto até o fim de jogo) ────────────────────
        var restartGo = MakeImage("RestartButton", root.transform, buttonRed, FallbackButtonRed);
        PlaceByAspect(restartGo.GetComponent<RectTransform>(), buttonRed,
                      SubmitFallbackAspect, SubmitWidthFrac, SubmitTopFrac, size);
        refs.Restart = MakeButton(restartGo);
        MakeButtonLabel(restartGo, "RestartLabel", "REINICIAR");
        // Fica sobre o botão de envio: no fim de jogo o UIController desativa o
        // envio e ativa este, então nunca aparecem os dois ao mesmo tempo.
        restartGo.SetActive(false);

        return refs;
    }

    private static GameObject MakeArea(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        // Margem para as bordas rasgadas da arte — texto encostado nelas some.
        Stretch(go.GetComponent<RectTransform>(),
                new Vector2(0.07f, 0.10f), new Vector2(0.93f, 0.90f));
        return go;
    }

    private static Button MakeButton(GameObject go)
    {
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = go.GetComponent<Image>();
        btn.transition    = Selectable.Transition.ColorTint;
        var c = btn.colors;
        c.normalColor      = Color.white;                         // não tinge o sprite
        c.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        c.pressedColor     = new Color(0.80f, 0.80f, 0.80f, 1f);
        c.disabledColor    = new Color(0.55f, 0.55f, 0.55f, 0.7f);
        c.fadeDuration     = 0.10f;
        btn.colors = c;
        return btn;
    }

    private static TMP_Text MakeButtonLabel(GameObject parent, string name, string text)
    {
        var label = MakeText(name, parent.transform, text, 34f, LabelOnRed, TextAlignmentOptions.Center);
        label.fontStyle        = FontStyles.Bold;
        label.characterSpacing = 6f;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        // Auto-size: a faixa livre é estreita e o rótulo troca entre "ENVIAR" e
        // "RESPONDER" em runtime — corpo fixo estouraria a caixa na palavra maior.
        label.enableAutoSizing = true;
        label.fontSizeMin      = 14f;
        label.fontSizeMax      = 36f;
        // Empurrado para a direita: medindo o sprite tratado, as algemas ocupam
        // de ~0.36 a ~0.64 da largura. Começar em 0.66 é o primeiro ponto livre
        // depois do punho direito; um rótulo centralizado cairia em cima do desenho.
        Stretch(label.rectTransform, new Vector2(0.66f, 0f), new Vector2(0.97f, 1f));
        return label;
    }

    /// <summary>
    /// TMP_InputField com o viewport que o componente exige — sem ele o texto
    /// vaza para fora do papel ao passar da última linha.
    /// </summary>
    private static TMP_InputField BuildInputField(Transform parent)
    {
        var go = new GameObject("StatementInputField",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        Stretch(go.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);

        // Fundo transparente, e NÃO Image desabilitada: um Image desativado deixa
        // de ser raycast target, e aí clicar no papel não daria foco ao campo —
        // só a área exata das letras responderia. Alpha 0 mantém a área clicável
        // inteira sem desenhar nada por cima da textura do papel.
        var bg = go.GetComponent<Image>();
        bg.color         = new Color(0f, 0f, 0f, 0f);
        bg.raycastTarget = true;

        var viewportGo = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
        viewportGo.transform.SetParent(go.transform, false);
        var viewportRt = viewportGo.GetComponent<RectTransform>();
        Stretch(viewportRt, Vector2.zero, Vector2.one);

        var placeholder = MakeText("Placeholder", viewportGo.transform, PlaceholderText,
                                   27f, InkFaded, TextAlignmentOptions.TopLeft);
        placeholder.fontStyle = FontStyles.Italic;
        Stretch(placeholder.rectTransform, Vector2.zero, Vector2.one);

        var text = MakeText("Text", viewportGo.transform, string.Empty,
                            27f, InkDark, TextAlignmentOptions.TopLeft);
        Stretch(text.rectTransform, Vector2.zero, Vector2.one);

        var input = go.AddComponent<TMP_InputField>();
        input.textViewport  = viewportRt;
        input.textComponent = text as TextMeshProUGUI;
        input.placeholder   = placeholder;
        input.targetGraphic = bg;
        // Depoimento é texto corrido: Enter quebra linha. Consequência: onSubmit
        // não dispara no Enter — enviar é pelo botão.
        input.lineType       = TMP_InputField.LineType.MultiLineNewline;
        input.characterLimit = 500;
        // richText off: sem isso o jogador digitaria <color=...> e formataria o
        // próprio depoimento, ou quebraria o parser do backend.
        input.richText         = false;
        input.customCaretColor = true;
        input.caretColor       = InkDark;
        input.caretWidth       = 2;
        input.selectionColor   = new Color(0.35f, 0.32f, 0.25f, 0.35f);
        input.restoreOriginalTextOnEscape = false;
        return input;
    }

    private static Button MakeSendProxy(Transform parent)
    {
        var go = new GameObject("SendProxy (oculto)", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0f);
        rt.sizeDelta = new Vector2(1f, 1f);

        var img = go.GetComponent<Image>();
        img.color         = new Color(0f, 0f, 0f, 0f);
        img.raycastTarget = false;      // nunca clicável pelo jogador

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.transition    = Selectable.Transition.None;
        return btn;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Religação
    // ═══════════════════════════════════════════════════════════════════════

    private static void WireUIController(UIController ui, Refs refs, Button proxy)
    {
        var so = new SerializedObject(ui);
        Set(so, "playerInputField", refs.Input);
        Set(so, "sendButton",       proxy);
        Set(so, "statusText",       refs.StatusText);
        Set(so, "detectiveText",    refs.DetectiveText);
        Set(so, "restartButton",    refs.Restart);
        // CanvasGroup PRÓPRIO da prancheta para o loading. Não pode ser o mesmo
        // que o InterrogationUIToggle usa no fade: as duas lógicas escrevendo no
        // mesmo alpha/interactable foi um bug já corrigido em GuiltySceneSetupUI.
        Set(so, "canvasGroup",      refs.Group);
        so.ApplyModifiedProperties();
    }

    private static void WireToggle(InterrogationUIToggle toggle, Refs refs, GameObject canvasGo)
    {
        if (toggle == null)
        {
            Debug.LogWarning("[InterrogMenu] Sem InterrogationUIToggle no Canvas — " +
                             "a prancheta ficará sempre visível (TAB não vai alternar).");
            return;
        }

        // O toggle faz o fade no CanvasGroup do próprio Canvas, e o SetActive no
        // chatPanelRoot. Mantida a separação descrita em WireUIController.
        var canvasGroup = canvasGo.GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = canvasGo.AddComponent<CanvasGroup>();

        var so = new SerializedObject(toggle);
        Set(so, "chatPanelRoot", refs.Root);
        Set(so, "canvasGroup",   canvasGroup);
        so.ApplyModifiedProperties();
    }

    private static void WireMenuController(GameObject canvasGo, Refs refs, Button proxy)
    {
        var ctrl = canvasGo.GetComponent<InterrogationMenuController>();
        if (ctrl == null) ctrl = canvasGo.AddComponent<InterrogationMenuController>();

        var so = new SerializedObject(ctrl);
        Set(so, "detectiveArea",       refs.DetectiveArea);
        Set(so, "writingArea",         refs.WritingArea);
        Set(so, "statementInputField", refs.Input);
        Set(so, "submitButton",        refs.Submit);
        Set(so, "submitLabel",         refs.SubmitLabel);
        Set(so, "sendProxy",           proxy);
        so.ApplyModifiedProperties();
    }

    /// <summary>
    /// Atribuição via SerializedObject porque os campos são [SerializeField]
    /// privados — atribuir direto não persistiria na serialização da cena.
    /// Mesmo padrão já usado em GuiltySceneSetupUI.
    /// </summary>
    private static void Set(SerializedObject so, string field, Object value)
    {
        var prop = so.FindProperty(field);
        if (prop == null)
        {
            Debug.LogWarning($"[InterrogMenu] Campo '{field}' não existe em " +
                             $"{so.targetObject.GetType().Name} — verifique no Inspector.");
            return;
        }
        prop.objectReferenceValue = value;
    }

    /// <summary>
    /// Desativa e renomeia o painel antigo em vez de apagar. É trabalho de
    /// outra pessoa no repositório; se a prancheta não agradar, reverter é
    /// reativar um GameObject, não recuperar do git.
    /// </summary>
    private static GameObject RetireOldPanel(InterrogationUIToggle toggle, GameObject canvasGo)
    {
        GameObject panel = null;

        if (toggle != null)
        {
            var so = new SerializedObject(toggle);
            var prop = so.FindProperty("chatPanelRoot");
            if (prop != null) panel = prop.objectReferenceValue as GameObject;
        }

        if (panel == null)
        {
            var t = canvasGo.transform.Find("Background Panel");
            if (t != null) panel = t.gameObject;
        }

        if (panel == null || panel.name.StartsWith("[ANTIGO]")) return panel;

        Undo.RecordObject(panel, "Retire Old Panel");
        panel.name = "[ANTIGO] " + panel.name;
        panel.SetActive(false);
        return panel;
    }

    private static void RemoveExisting(Transform parent)
    {
        var t = parent.Find(RootName);
        if (t != null)
        {
            Undo.DestroyObjectImmediate(t.gameObject);
            Debug.Log("[InterrogMenu] Prancheta anterior removida antes de remontar.");
        }
        var p = parent.Find("SendProxy (oculto)");
        if (p != null) Undo.DestroyObjectImmediate(p.gameObject);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Peças e utilitários
    // ═══════════════════════════════════════════════════════════════════════

    private static Sprite[] LoadSprites(ref int missing)
    {
        return new[]
        {
            LoadSprite(ClipboardSprite, ref missing),
            LoadSprite(StatusSprite,    ref missing),
            LoadSprite(StatementSprite, ref missing),
            LoadSprite(ButtonSprite,    ref missing),
        };
    }

    private static GameObject MakeImage(string name, Transform parent, Sprite sprite, Color fallback)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);

        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.type   = Image.Type.Simple;
        // preserveAspect FALSE de propósito: com ele o sprite encolhe dentro do
        // RectTransform, e como os filhos são ancorados ao RECT (não ao desenho),
        // eles descolariam da arte. O rect já recebe a proporção certa.
        img.preserveAspect = false;
        img.color = sprite != null ? Color.white : fallback;
        return go;
    }

    private static TMP_Text MakeText(string name, Transform parent, string content,
                                     float size, Color color, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var t = go.AddComponent<TextMeshProUGUI>();
        t.text             = content;
        t.fontSize         = size;
        t.color            = color;
        t.alignment        = align;
        t.raycastTarget    = false;   // texto decorativo não rouba clique do papel
        t.textWrappingMode = TextWrappingModes.Normal;
        return t;
    }

    /// <summary>
    /// Ancora a peça com a largura pedida e a ALTURA que preserva a proporção do
    /// sprite. Continua sendo posicionamento por âncora, então os filhos
    /// acompanham a prancheta quando o CanvasScaler muda a escala.
    /// </summary>
    private static void PlaceByAspect(RectTransform rt, Sprite sprite, float fallbackAspect,
                                      float widthFrac, float topFrac, Vector2 clipboardSize)
    {
        float aspect = (sprite != null && sprite.rect.height > 0f)
            ? sprite.rect.width / sprite.rect.height
            : fallbackAspect;

        float heightFrac = widthFrac * (clipboardSize.x / clipboardSize.y) / aspect;

        float xMin = 0.5f - widthFrac * 0.5f;
        float yMax = 1f - topFrac;
        float yMin = yMax - heightFrac;

        if (yMin < 0f)
            Debug.LogWarning($"[InterrogMenu] '{rt.name}' passou da base da prancheta " +
                             $"(altura {heightFrac:P0} a partir de {topFrac:P0} do topo). " +
                             "Reduza a fração de largura dessa peça no topo do builder.");

        rt.anchorMin        = new Vector2(xMin, yMin);
        rt.anchorMax        = new Vector2(xMin + widthFrac, yMax);
        rt.offsetMin        = Vector2.zero;
        rt.offsetMax        = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;
    }

    private static void Stretch(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax)
    {
        rt.anchorMin        = anchorMin;
        rt.anchorMax        = anchorMax;
        rt.offsetMin        = Vector2.zero;
        rt.offsetMax        = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;
    }

    private static Vector2 ResolveClipboardSize(Sprite clipboard)
    {
        float aspect = ClipboardFallbackAspect;
        if (clipboard != null && clipboard.rect.height > 0f)
            aspect = clipboard.rect.width / clipboard.rect.height;
        return new Vector2(ClipboardHeight * aspect, ClipboardHeight);
    }

    private static void ConfigureScaler(CanvasScaler scaler)
    {
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        // match=1 (altura): a prancheta é um objeto ALTO. Casando pela altura ela
        // ocupa a mesma fatia vertical em 16:9 e em 4:3, em vez de estourar por
        // baixo quando a tela fica mais estreita.
        scaler.screenMatchMode    = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;
    }

    /// <summary>
    /// Carrega o sprite e corrige o import se o PNG entrou como textura comum —
    /// nesse caso o arquivo existe mas LoadAssetAtPath&lt;Sprite&gt; devolve null,
    /// e o motivo não é óbvio olhando a pasta.
    /// </summary>
    private static Sprite LoadSprite(string path, ref int missing)
    {
        if (!File.Exists(path))
        {
            Debug.LogWarning($"[InterrogMenu] Sprite ausente: {path} — usando cor sólida.");
            missing++;
            return null;
        }

        if (AssetImporter.GetAtPath(path) is TextureImporter imp &&
            imp.textureType != TextureImporterType.Sprite)
        {
            imp.textureType         = TextureImporterType.Sprite;
            imp.spriteImportMode    = SpriteImportMode.Single;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled       = false;
            imp.filterMode          = FilterMode.Bilinear;
            imp.SaveAndReimport();
            Debug.Log($"[InterrogMenu] {Path.GetFileName(path)} reimportado como Sprite (UI).");
        }

        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
        {
            Debug.LogWarning($"[InterrogMenu] {path} existe mas não pôde ser lido como Sprite.");
            missing++;
        }
        return sprite;
    }

    private static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include) != null) return;

        var go = new GameObject("EventSystem", typeof(EventSystem));
        Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");

        // O projeto usa o Input System novo; o StandaloneInputModule antigo
        // deixaria o Canvas inerte.
        var moduleType = System.Type.GetType(
            "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
        if (moduleType != null) go.AddComponent(moduleType);
        else go.AddComponent<StandaloneInputModule>();

        Debug.Log("[InterrogMenu] EventSystem criado — sem ele nenhum clique chega na UI.");
    }

    private static void ReportSpriteStatus(int missing)
    {
        if (missing == 0) return;
        Debug.LogWarning(
            $"[InterrogMenu] {missing} de 4 sprites não aplicados. Coloque os PNGs em " +
            $"{SpriteFolder}/ como UI_Clipboard.png, UI_PaperAged.png, " +
            "UI_PaperCrumpled.png e UI_ButtonRed.png, e rode o menu de novo. " +
            "O layout já está correto — falta só a arte.");
    }
}
