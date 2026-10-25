using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

/// <summary>
/// Pause e fim de jogo. Não decide nada sobre suspeita, turnos ou vitória —
/// só REAGE ao que o backend decide (game_rules.py) e o GameManager publica.
///
/// REGRAS DE FIM (todas no backend, verificadas a cada turno):
///   DERROTA  suspeita = 100%                         (imediata, em qualquer turno)
///   DERROTA  suspeita >= 80% por 3 turnos seguidos
///   VITÓRIA  a partir do turno 8 com suspeita <= 30%  ("liberado")
///   LIMITE   no turno 12: suspeita >= 60% = derrota, senão vitória
/// Antes a vitória era avaliada aqui (turno >= 8 e suspeita <= 30) e a derrota
/// no backend (3 turnos >= 90): duas definições do mesmo jogo. Agora existe uma.
/// </summary>
public class GameFlowController : MonoBehaviour
{
    public enum EndKind { None, Victory, Defeat }

    [Header("Telas")]
    [SerializeField] private CanvasGroup pauseOverlay;
    [SerializeField] private CanvasGroup endOverlay;
    [SerializeField] private TMPro.TMP_Text endTitle;
    [SerializeField] private TMPro.TMP_Text endBody;

    [Header("Transição")]
    [SerializeField] private float fadeDuration = 0.28f;

    [Header("Cena de menu")]
    [SerializeField] private string mainMenuScene = "MainMenu";

    public bool IsPaused { get; private set; }
    public EndKind Ended { get; private set; } = EndKind.None;

    private InputAction pauseAction;
    private InterrogationUIToggle interrogationToggle;
    private bool chatWasOpen;
    private bool subscribed;

    private void Awake()
    {
        pauseAction = new InputAction("Pause", InputActionType.Button);
        pauseAction.AddBinding("<Keyboard>/escape");
        pauseAction.performed += _ => TogglePause();

        interrogationToggle = FindFirstObjectByType<InterrogationUIToggle>();

        HideInstant(pauseOverlay);
        HideInstant(endOverlay);
    }

    private void OnEnable()
    {
        pauseAction.Enable();
        InterrogationUIToggle.OnMenuToggled += OnChatToggled;
    }

    private void OnDisable()
    {
        pauseAction.Disable();
        InterrogationUIToggle.OnMenuToggled -= OnChatToggled;
    }

    private void OnDestroy()
    {
        Unsubscribe();
        pauseAction?.Dispose();
        // um fim de jogo ou troca de cena nunca pode deixar o tempo parado
        Time.timeScale = 1f;
    }

    // Assinatura no Start pelo mesmo motivo do SuspicionVisualFeedback:
    // OnEnable pode rodar antes do Awake do ApiClient/GameManager.
    private void Start()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnGameOver += OnGameOver;
        else
            Debug.LogWarning("[GameFlow] GameManager.Instance ausente — o fim de jogo não será detectado.");

        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed) return;
        if (GameManager.Instance != null) GameManager.Instance.OnGameOver -= OnGameOver;
        subscribed = false;
    }

    private void OnChatToggled(bool open) => chatWasOpen = open;

    // ─────────────────────────────── PAUSE ───────────────────────────────

    /// <summary>
    /// Esc sempre abre o pause, inclusive com o painel de interrogatório aberto.
    /// Enquanto pausado o TAB fica desligado, então não dá para abrir o chat por
    /// baixo do overlay. Ao continuar, o cursor volta para o estado que o
    /// InterrogationUIToggle esperava.
    /// </summary>
    public void TogglePause()
    {
        if (Ended != EndKind.None) return;   // fim de jogo não pausa
        // Esc com o tutorial aberto fecha o tutorial, como qualquer janela. Antes
        // a pausa abria POR BAIXO dele, invisível, e o jogador não via o menu.
        if (TutorialOverlay.AnyOpen && !IsPaused)
        {
            TutorialOverlay.Current.Close();
            return;
        }
        SetPaused(!IsPaused);
    }

    public void SetPaused(bool paused)
    {
        if (IsPaused == paused) return;
        IsPaused = paused;

        Time.timeScale = paused ? 0f : 1f;

        // desliga o TAB enquanto pausado
        if (interrogationToggle != null) interrogationToggle.enabled = !paused;

        if (paused)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Show(pauseOverlay);
        }
        else
        {
            Hide(pauseOverlay);
            // devolve o cursor ao que o painel de interrogatório espera
            Cursor.lockState = chatWasOpen ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = chatWasOpen;
        }
    }

    public void Resume() => SetPaused(false);

    public void RestartRun()
    {
        Time.timeScale = 1f;
        GameManager.Instance?.ResetGame();
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void QuitToMenu()
    {
        // Sair no meio também encerra a partida. Sem isto o GameManager (que
        // sobrevive à troca de cena) levava a sessão, a suspeita e até o
        // IsGameOver para a próxima partida — que podia já nascer perdida.
        GameManager.Instance?.ResetGame();
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        SceneManager.LoadScene(mainMenuScene);
    }

    // ────────────────────────── VITÓRIA / DERROTA ──────────────────────────

    private void OnGameOver()
    {
        var gm = GameManager.Instance;
        bool victory = gm != null && gm.Result == "vitoria";
        // Registra o caso resolvido aqui, e não no EndRun: o EndRun também é
        // chamado por ferramentas de validação sem backend, e só a vitória
        // decidida pelo backend pode desbloquear o próximo caso. O Result ainda
        // está preenchido (só o ResetGame o limpa).
        if (victory) CaseProgress.MarkSolved(CaseSelection.CaseId);
        EndRun(victory ? EndKind.Victory : EndKind.Defeat);
    }

    /// <summary>
    /// Público para que ferramentas de validação possam abrir a tela de fim sem
    /// precisar do backend. O jogo em si só chega aqui pelos eventos acima.
    /// </summary>
    public void EndRun(EndKind kind)
    {
        if (Ended != EndKind.None) return;
        Ended = kind;

        if (IsPaused) SetPaused(false);
        if (interrogationToggle != null) interrogationToggle.enabled = false;

        // A partida acabou: a câmera para de seguir o mouse. Com o cursor livre
        // para clicar nos botões, ela continuava girando por trás da tela de fim.
        foreach (var look in FindObjectsByType<PlayerCameraLook>(FindObjectsSortMode.None))
            look.enabled = false;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (endTitle != null)
            endTitle.text = kind == EndKind.Victory ? "LIBERADO" : "CASO ENCERRADO";

        if (endBody != null)
            endBody.text = EndMessage(kind, GameManager.Instance != null ? GameManager.Instance.EndReason : "");

        Show(endOverlay);
        Debug.Log($"[GameFlow] fim de jogo: {kind}");
    }

    private static string EndMessage(EndKind kind, string reason)
    {
        switch (reason)
        {
            case "suspeita_maxima":
                return "A suspeita chegou a 100%.\nO detetive fechou o caso contra você.";
            case "suspeita_sustentada":
                return "A suspeita se manteve em 80% ou mais por três turnos seguidos.\n" +
                       "O detetive fechou o caso contra você.";
            case "liberado":
                return "Você sustentou o depoimento com a suspeita baixa.\n" +
                       "O detetive não reuniu o suficiente para sustentar a acusação.";
            case "limite_de_turnos":
                return kind == EndKind.Victory
                    ? "O interrogatório chegou ao limite de turnos sem provas contra você.\nVocê foi liberado."
                    : "O interrogatório chegou ao limite de turnos com a suspeita alta.\nO detetive fechou o caso contra você.";
            default:
                return kind == EndKind.Victory
                    ? "O detetive não reuniu o suficiente para sustentar a acusação."
                    : "O detetive fechou o caso contra você.";
        }
    }

    // ───────────────────────────── transições ─────────────────────────────
    // unscaledDeltaTime: com Time.timeScale = 0 o fade do pause não andaria.

    private void Show(CanvasGroup g)
    {
        if (g == null) return;
        g.gameObject.SetActive(true);
        g.blocksRaycasts = true;
        g.interactable = true;
        StartCoroutine(FadeTo(g, 1f, true));
    }

    private void Hide(CanvasGroup g)
    {
        if (g == null) return;
        g.blocksRaycasts = false;
        g.interactable = false;
        StartCoroutine(FadeTo(g, 0f, false));
    }

    private static void HideInstant(CanvasGroup g)
    {
        if (g == null) return;
        g.alpha = 0f;
        g.blocksRaycasts = false;
        g.interactable = false;
        g.gameObject.SetActive(false);
    }

    private IEnumerator FadeTo(CanvasGroup g, float target, bool keepActive)
    {
        float from = g.alpha;
        var rect = g.transform as RectTransform;
        float t = 0f;

        while (t < fadeDuration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / fadeDuration);
            k = k * k * (3f - 2f * k);                 // smoothstep, sem bounce
            g.alpha = Mathf.Lerp(from, target, k);
            if (rect != null)
            {
                // leve escala, quase imperceptível — só tira o corte seco
                float s = Mathf.Lerp(target > 0.5f ? 0.985f : 1f, target > 0.5f ? 1f : 0.985f, k);
                rect.localScale = new Vector3(s, s, 1f);
            }
            yield return null;
        }

        g.alpha = target;
        if (rect != null) rect.localScale = Vector3.one;
        if (!keepActive && target <= 0f) g.gameObject.SetActive(false);
    }
}
