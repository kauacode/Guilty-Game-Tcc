using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Controla a seleção de casos com máquina de dois estados:
///
/// BoardBrowsing  — navega entre os casos; Confirm abre o dossiê.
/// DossierOpen    — painel lateral visível; Confirm carrega cena; Cancel fecha painel.
/// </summary>
public class BoardSelectionController : MonoBehaviour
{
    public static BoardSelectionController Instance { get; private set; }

    // ── Referências ─────────────────────────────────────────────────────────

    [Header("Casos (auto-descoberto e ordenado por X se vazio)")]
    [SerializeField] private CaseItem[] cases;

    [Header("Câmera")]
    [SerializeField] private Camera boardCamera;

    [Header("Transição de fade")]
    [SerializeField] private CanvasGroup fadeOverlay;
    [SerializeField] private float       fadeDuration = 0.4f;

    [Header("UI")]
    [SerializeField] private DossierPanel      dossierPanel;
    [SerializeField] private TextMeshProUGUI   instructionText;

    [Header("Navegação")]
    [Tooltip("Cena para onde o botão/tecla de voltar leva.")]
    [SerializeField] private string mainMenuScene = "MainMenu";

    private const string ScreenTitle = "SELECIONE UM CASO";

    // ── Estado ──────────────────────────────────────────────────────────────

    private enum State { BoardBrowsing, DossierOpen }
    private State currentState = State.BoardBrowsing;

    private string[] caseIds = new string[0];
    private int  currentIndex;
    private bool transitioning;
    private bool keyboardPriority;

    // ── Input ───────────────────────────────────────────────────────────────

    private InputAction actionRight;
    private InputAction actionLeft;
    private InputAction actionConfirm;
    private InputAction actionCancel;
    private InputAction actionBack;     // só ESC: voltar ao menu a partir do quadro

    private Texture2D cursorTex;
    private RectTransform backButtonRect;
    private Canvas backCanvas;

    // ── Textos de instrução ─────────────────────────────────────────────────

    private const string INSTR_BROWSE  =
        "[ ← / → ]  ou  [ MOUSE ]  Selecionar Caso     |     [ ENTER ]  ou  [ CLIQUE ]  Abrir Dossiê     |     [ ESC ]  Voltar ao Menu";
    private const string INSTR_DOSSIER =
        "[ ENTER ]  ou  botão INICIAR  Iniciar Interrogatório     |     [ ESC ]  ou  VOLTAR  Retornar ao Quadro";
    private const string INSTR_DOSSIER_LOCKED =
        "Dossiê bloqueado     |     [ ESC ]  ou  VOLTAR  Retornar ao Quadro";

    // ── Ciclo de vida ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        Cursor.lockState = CursorLockMode.Confined;
        Cursor.visible   = true;
        Time.timeScale   = 1f;

        if (boardCamera == null) boardCamera = Camera.main;

        if (fadeOverlay != null)
        {
            fadeOverlay.alpha          = 1f;
            fadeOverlay.blocksRaycasts = true;
        }

        if (cases == null || cases.Length == 0)
            cases = FindObjectsByType<CaseItem>(FindObjectsSortMode.None);

        System.Array.Sort(cases,
            (a, b) => a.transform.position.x.CompareTo(b.transform.position.x));

        // Desbloqueio progressivo: vencer um caso abre o seguinte. A ordem é a
        // dos caseId, não a posição no quadro.
        caseIds = System.Array.ConvertAll(cases, c => c.caseId);
        foreach (var c in cases)
            c.SetEffectiveLock(CaseProgress.IsLocked(c.caseId, c.caseData != null && c.caseData.isLocked, caseIds));

        // Input Actions
        actionRight = new InputAction("Right", InputActionType.Button);
        actionRight.AddBinding("<Keyboard>/d");
        actionRight.AddBinding("<Keyboard>/rightArrow");

        actionLeft = new InputAction("Left", InputActionType.Button);
        actionLeft.AddBinding("<Keyboard>/a");
        actionLeft.AddBinding("<Keyboard>/leftArrow");

        actionConfirm = new InputAction("Confirm", InputActionType.Button);
        actionConfirm.AddBinding("<Keyboard>/space");
        actionConfirm.AddBinding("<Keyboard>/enter");
        actionConfirm.AddBinding("<Keyboard>/numpadEnter");
        actionConfirm.AddBinding("<Mouse>/leftButton");

        actionCancel = new InputAction("Cancel", InputActionType.Button);
        actionCancel.AddBinding("<Keyboard>/escape");
        actionCancel.AddBinding("<Mouse>/rightButton");

        // O botão direito fecha o dossiê, mas NÃO sai da tela: voltar ao menu é
        // uma decisão deliberada (ESC ou botão).
        actionBack = new InputAction("Back", InputActionType.Button);
        actionBack.AddBinding("<Keyboard>/escape");
    }

    private void OnEnable()
    {
        actionRight?.Enable();
        actionLeft?.Enable();
        actionConfirm?.Enable();
        actionCancel?.Enable();
        actionBack?.Enable();
    }

    private void OnDisable()
    {
        actionRight?.Disable();
        actionLeft?.Disable();
        actionConfirm?.Disable();
        actionCancel?.Disable();
        actionBack?.Disable();
    }

    private void OnDestroy()
    {
        actionRight?.Dispose();
        actionLeft?.Dispose();
        actionConfirm?.Dispose();
        actionCancel?.Dispose();
        actionBack?.Dispose();
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
        if (cursorTex != null) Destroy(cursorTex);
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        cursorTex = GenerateCursorTexture(48);
        Cursor.SetCursor(cursorTex, new Vector2(24, 24), CursorMode.Auto);

        if (cases.Length > 0) cases[currentIndex].SetFocused(true);

        // Subscribes a eventos do painel
        if (dossierPanel != null)
        {
            dossierPanel.OnConfirmRequested += ConfirmDossier;
            dossierPanel.OnBackRequested    += CloseDossier;
        }

        SetInstructionText(INSTR_BROWSE);
        BuildScreenChrome();
        StartCoroutine(FadeIn());
    }

    private void Update()
    {
        if (transitioning || cases.Length == 0) return;

        switch (currentState)
        {
            case State.BoardBrowsing:
                HandleKeyboard();
                HandleMouseRaycast();
                if (actionBack.WasPressedThisFrame()) { GoBackToMenu(); break; }
                // Clique no MENU tratado aqui, por posição, sem depender do
                // EventSystem/GraphicRaycaster da cena (o Button não respondia).
                if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && PointerOverBackButton())
                { GoBackToMenu(); break; }
                if (ConfirmPressed()) OpenDossier();
                break;

            case State.DossierOpen:
                // Só confirma por ENTER/ESPAÇO ou clicando no botão INICIAR; clicar
                // em qualquer outro ponto não faz nada (antes iniciava a partida).
                if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && dossierPanel != null)
                {
                    Vector2 mp = Mouse.current.position.ReadValue();
                    if (dossierPanel.IsOverConfirm(mp)) { ConfirmDossier(); break; }
                    if (dossierPanel.IsOverBack(mp))    { CloseDossier();   break; }
                }
                if (KeyboardConfirmPressed()) ConfirmDossier();
                if (actionCancel.WasPressedThisFrame())  CloseDossier();
                break;
        }
    }

    // ── Navegação (apenas no estado Browse) ─────────────────────────────────

    private void HandleKeyboard()
    {
        if (actionRight.WasPressedThisFrame()) { MoveFocus(+1); keyboardPriority = true; }
        if (actionLeft.WasPressedThisFrame())  { MoveFocus(-1); keyboardPriority = true; }
    }

    private void MoveFocus(int delta)
    {
        int next = Mathf.Clamp(currentIndex + delta, 0, cases.Length - 1);
        SetFocus(next);
    }

    private void HandleMouseRaycast()
    {
        if (boardCamera == null || Mouse.current == null) return;

        if (Mouse.current.delta.ReadValue().sqrMagnitude > 0.5f)
            keyboardPriority = false;

        if (keyboardPriority) return;

        Vector2 mp  = Mouse.current.position.ReadValue();
        Ray     ray = boardCamera.ScreenPointToRay(mp);

        if (!Physics.Raycast(ray, out RaycastHit hit, 10f)) return;

        for (int i = 0; i < cases.Length; i++)
        {
            if (hit.collider.gameObject == cases[i].gameObject ||
                hit.collider.transform.IsChildOf(cases[i].transform))
            {
                SetFocus(i);
                return;
            }
        }
    }

    private void SetFocus(int index)
    {
        if (index == currentIndex && cases[currentIndex].IsFocused) return;
        cases[currentIndex].SetFocused(false);
        currentIndex = index;
        cases[currentIndex].SetFocused(true);
        UiAudio.Play(UiAudio.Som.Hover);
    }

    // ── Estado: abrir / fechar dossiê ────────────────────────────────────────

    private void OpenDossier()
    {
        var item = cases[currentIndex];

        // Bloqueado também abre, só para leitura: o jogador vê o que vem pela
        // frente e o painel diz qual caso falta vencer. Iniciar segue barrado
        // no ConfirmDossier.
        currentState = State.DossierOpen;
        UiAudio.Play(UiAudio.Som.DossieAbrir);
        dossierPanel?.Show(item.caseData, item.isLocked ? LockedReason(item) : null);
        SetInstructionText(item.isLocked ? INSTR_DOSSIER_LOCKED : INSTR_DOSSIER);
    }

    private string LockedReason(CaseItem item)
    {
        string previous = CaseProgress.PreviousCaseId(item.caseId, caseIds);
        return previous != null ? $"Complete o {previous} para desbloquear" : "Dossiê indisponível";
    }

    private void CloseDossier()
    {
        currentState = State.BoardBrowsing;
        UiAudio.Play(UiAudio.Som.DossieFechar);
        dossierPanel?.Hide();
        SetInstructionText(INSTR_BROWSE);
    }

    // ── Confirmação (carrega a cena) ─────────────────────────────────────────

    private void ConfirmDossier()
    {
        if (transitioning) return;

        var item = cases[currentIndex];
        // ENTER/ESPAÇO e o clique por posição chegam aqui sem passar pelo botão
        // (que só fica desabilitado): a trava de verdade é esta.
        if (item.isLocked)
        {
            Debug.Log($"[Board] Caso {item.caseId} bloqueado.");
            return;
        }
        if (!SceneExists(item.targetSceneName))
        {
            Debug.LogWarning($"[Board] '{item.targetSceneName}' não está no Build Settings.");
            return;
        }

        // A partida usa o caso escolhido aqui (vai no case_id de cada fala).
        CaseSelection.CaseId = item.caseId;
        // Sem isto os casos 02/03 herdavam o nome e a 1a pergunta do caso 01.
        CaseSelection.PlayerName  = item.caseData?.playerName ?? "";
        CaseSelection.OpeningLine = item.caseData?.openingLine ?? "";
        CaseSelection.PlayerIntro = item.caseData?.playerIntro ?? "";

        UiAudio.Play(UiAudio.Som.IniciarCaso);
        transitioning = true;
        StartCoroutine(FadeOutThenLoad(item.targetSceneName));
    }

    /// <summary>
    /// Volta ao menu principal. Nada de partida existe nesta tela (ela só
    /// escolhe o caso), então não há o que descartar; quem inicia a partida é o
    /// ConfirmDossier — voltar nunca começa uma.
    /// </summary>
    public void GoBackToMenu()
    {
        if (transitioning) return;
        if (!SceneExists(mainMenuScene))
        {
            Debug.LogWarning($"[Board] '{mainMenuScene}' não está no Build Settings.");
            return;
        }
        transitioning = true;
        StartCoroutine(FadeOutThenLoad(mainMenuScene));
    }

    /// <summary>
    /// Confirmar por tecla ou por clique no mundo. Um clique que caiu num botão
    /// da UI (VOLTAR, os botões do dossiê) é do botão — sem esta guarda o mesmo
    /// clique também abria/confirmava o dossiê por baixo.
    /// </summary>
    private bool ConfirmPressed()
    {
        if (!actionConfirm.WasPressedThisFrame()) return false;
        bool fromMouse = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        if (fromMouse && (PointerOverBackButton() ||
            (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())))
            return false;
        return true;
    }

    private static bool KeyboardConfirmPressed()
    {
        var kb = Keyboard.current;
        return kb != null && (kb.enterKey.wasPressedThisFrame ||
                              kb.numpadEnterKey.wasPressedThisFrame ||
                              kb.spaceKey.wasPressedThisFrame);
    }

    private bool PointerOverBackButton()
    {
        if (backButtonRect == null || Mouse.current == null) return false;
        Camera cam = (backCanvas != null && backCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            ? backCanvas.worldCamera : null;
        return RectTransformUtility.RectangleContainsScreenPoint(
            backButtonRect, Mouse.current.position.ReadValue(), cam);
    }

    // ── Título e botão de voltar (criados em código: a cena é gerada por Editor) ──

    private void BuildScreenChrome()
    {
        if (instructionText == null) return;
        var canvas = instructionText.canvas != null ? instructionText.canvas.rootCanvas : null;
        if (canvas == null) return;

        if (canvas.GetComponent<GraphicRaycaster>() == null)
            canvas.gameObject.AddComponent<GraphicRaycaster>();

        // Título
        if (canvas.transform.Find("[TituloSelecao]") == null)
        {
            var go = new GameObject("[TituloSelecao]", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            var title = go.AddComponent<TextMeshProUGUI>();
            title.font = instructionText.font;
            title.text = ScreenTitle;
            title.fontSize = 64f;
            title.fontStyle = FontStyles.Bold;
            title.characterSpacing = 8f;
            title.alignment = TextAlignmentOptions.Center;
            title.color = new Color(0.93f, 0.78f, 0.42f);
            title.raycastTarget = false;
            var rt = title.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -36f);
            rt.sizeDelta = new Vector2(1200f, 90f);
        }

        // Botão voltar
        if (canvas.transform.Find("[BotaoVoltarMenu]") == null)
        {
            var go = new GameObject("[BotaoVoltarMenu]", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(canvas.transform, false);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.07f, 0.06f, 0.05f, 0.85f);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(36f, -36f);
            rt.sizeDelta = new Vector2(230f, 56f);
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0.85f, 0.65f, 0.25f, 0.7f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(GoBackToMenu);
            backButtonRect = rt;
            backCanvas = canvas;

            var labelGo = new GameObject("Rotulo", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.font = instructionText.font;
            label.text = "← MENU";
            label.fontSize = 28f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(0.93f, 0.90f, 0.82f);
            label.raycastTarget = false;
            var lrt = label.rectTransform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
        }
    }

    // Chamado por BoardSelectionController.SelectCase (legado / externo)
    public void SelectCase(CaseItem item)
    {
        for (int i = 0; i < cases.Length; i++)
            if (cases[i] == item) { SetFocus(i); break; }

        OpenDossier();
    }

    // ── Transições de cena ───────────────────────────────────────────────────

    private IEnumerator FadeIn()
    {
        if (fadeOverlay == null) yield break;
        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / fadeDuration); k = k * k * (3f - 2f * k);
            fadeOverlay.alpha = 1f - k;
            yield return null;
        }
        fadeOverlay.alpha          = 0f;
        fadeOverlay.blocksRaycasts = false;
    }

    private IEnumerator FadeOutThenLoad(string sceneName)
    {
        if (fadeOverlay != null)
        {
            fadeOverlay.blocksRaycasts = true;
            float from = fadeOverlay.alpha, t = 0f;
            while (t < fadeDuration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / fadeDuration); k = k * k * (3f - 2f * k);
                fadeOverlay.alpha = Mathf.Lerp(from, 1f, k);
                yield return null;
            }
            fadeOverlay.alpha = 1f;
        }
        else yield return new WaitForSecondsRealtime(fadeDuration);

        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
        SceneManager.LoadScene(sceneName);
    }

    // ── Utilitários ──────────────────────────────────────────────────────────

    private void SetInstructionText(string text)
    {
        if (instructionText != null) instructionText.text = text;
    }

    private static bool SceneExists(string name)
    {
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            if (System.IO.Path.GetFileNameWithoutExtension(
                    SceneUtility.GetScenePathByBuildIndex(i)) == name) return true;
        return false;
    }

    // ── Cursor retículo ──────────────────────────────────────────────────────

    private static Texture2D GenerateCursorTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        float cx = size * 0.5f, cy = size * 0.5f;
        float outerR = size * 0.42f, innerR = outerR - size * 0.07f, dotR = size * 0.06f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
            float d  = Mathf.Sqrt(dx * dx + dy * dy);
            Color c;
            if (d >= innerR && d <= outerR)
            {
                float edge = Mathf.Min(d - innerR, outerR - d);
                c = new Color(1f, 1f, 1f, Mathf.Clamp01(edge / 1.2f));
            }
            else if (d <= dotR)
            {
                c = new Color(1f, 1f, 1f, Mathf.Clamp01((dotR - d) / 1.2f));
            }
            else if ((d >= innerR - 1.5f && d < innerR) || (d > outerR && d <= outerR + 1.5f))
            {
                float edge = Mathf.Min(Mathf.Abs(d - innerR), Mathf.Abs(d - outerR));
                c = new Color(0f, 0f, 0f, Mathf.Clamp01(1f - edge / 1.5f) * 0.55f);
            }
            else c = Color.clear;
            tex.SetPixel(x, y, c);
        }
        tex.Apply();
        return tex;
    }
}
