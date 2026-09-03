using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
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

    // ── Estado ──────────────────────────────────────────────────────────────

    private enum State { BoardBrowsing, DossierOpen }
    private State currentState = State.BoardBrowsing;

    private int  currentIndex;
    private bool transitioning;
    private bool keyboardPriority;

    // ── Input ───────────────────────────────────────────────────────────────

    private InputAction actionRight;
    private InputAction actionLeft;
    private InputAction actionConfirm;
    private InputAction actionCancel;

    private Texture2D cursorTex;

    // ── Textos de instrução ─────────────────────────────────────────────────

    private const string INSTR_BROWSE  =
        "[ ← / → ]  ou  [ MOUSE ]  Selecionar Caso     |     [ ENTER ]  ou  [ CLIQUE ]  Abrir Dossiê";
    private const string INSTR_DOSSIER =
        "[ ENTER / CLIQUE ]  Iniciar Interrogatório     |     [ ESC / VOLTAR ]  Retornar ao Quadro";

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
    }

    private void OnEnable()
    {
        actionRight?.Enable();
        actionLeft?.Enable();
        actionConfirm?.Enable();
        actionCancel?.Enable();
    }

    private void OnDisable()
    {
        actionRight?.Disable();
        actionLeft?.Disable();
        actionConfirm?.Disable();
        actionCancel?.Disable();
    }

    private void OnDestroy()
    {
        actionRight?.Dispose();
        actionLeft?.Dispose();
        actionConfirm?.Dispose();
        actionCancel?.Dispose();
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
                if (actionConfirm.WasPressedThisFrame()) OpenDossier();
                break;

            case State.DossierOpen:
                // clique no papel ativo → confirma
                if (actionConfirm.WasPressedThisFrame()) ConfirmDossier();
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
    }

    // ── Estado: abrir / fechar dossiê ────────────────────────────────────────

    private void OpenDossier()
    {
        var item = cases[currentIndex];
        if (item.isLocked)
        {
            Debug.Log($"[Board] Caso {item.caseId} bloqueado.");
            return;
        }

        currentState = State.DossierOpen;
        dossierPanel?.Show(item.caseData);
        SetInstructionText(INSTR_DOSSIER);
    }

    private void CloseDossier()
    {
        currentState = State.BoardBrowsing;
        dossierPanel?.Hide();
        SetInstructionText(INSTR_BROWSE);
    }

    // ── Confirmação (carrega a cena) ─────────────────────────────────────────

    private void ConfirmDossier()
    {
        if (transitioning) return;

        var item = cases[currentIndex];
        if (!SceneExists(item.targetSceneName))
        {
            Debug.LogWarning($"[Board] '{item.targetSceneName}' não está no Build Settings.");
            return;
        }

        transitioning = true;
        StartCoroutine(FadeOutThenLoad(item.targetSceneName));
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
