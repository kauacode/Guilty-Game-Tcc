using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

/// <summary>
/// Pause e fim de jogo. Não decide nada sobre suspeita — só REAGE ao que o
/// GameManager e o ApiClient já publicam.
///
/// REGRAS (levantadas do código, não supostas):
///
///   DERROTA — já existia. O backend liga fim_de_jogo quando a suspeita fica
///   >= 90 por 3 turnos seguidos (prompt_orchestrator.py). O GameManager
///   converte isso em OnGameOver. Como o jogador É o suspeito, suspeita alta
///   é o fracasso dele: o detetive fechou o caso contra ele.
///
///   VITÓRIA — NÃO existia no projeto. Nem o backend nem o Unity tinham
///   qualquer noção de ganhar. A regra abaixo é uma PROPOSTA, montada só com
///   dados que a API já devolve (id_turno e nivel_suspeita), sem tocar na
///   lógica de suspeita: aguentar victoryTurn turnos mantendo a suspeita em
///   até victoryMaxSuspicion significa que o interrogatório não pegou nada.
///
/// Não existe temporizador no jogo, então "tempo esgotado" não é uma derrota
/// possível hoje — seria preciso criar um cronômetro, o que mudaria o design.
/// </summary>
public class GameFlowController : MonoBehaviour
{
    public enum EndKind { None, Victory, Defeat }

    [Header("Telas")]
    [SerializeField] private CanvasGroup pauseOverlay;
    [SerializeField] private CanvasGroup endOverlay;
    [SerializeField] private TMPro.TMP_Text endTitle;
    [SerializeField] private TMPro.TMP_Text endBody;

    [Header("Regra de vitória (proposta — ver resumo da classe)")]
    [Tooltip("Turno a partir do qual o suspeito é liberado, se a suspeita estiver baixa.")]
    [SerializeField] private int victoryTurn = 8;
    [Tooltip("Suspeita máxima aceita para considerar que o interrogatório não pegou nada.")]
    [SerializeField] private int victoryMaxSuspicion = 30;

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
        if (ApiClient.Instance != null)
            ApiClient.Instance.OnResponseReceived += OnApiResponse;
        else
            Debug.LogWarning("[GameFlow] ApiClient.Instance ausente — a vitória não será avaliada.");

        if (GameManager.Instance != null)
            GameManager.Instance.OnGameOver += OnDefeat;
        else
            Debug.LogWarning("[GameFlow] GameManager.Instance ausente — a derrota não será detectada.");

        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed) return;
        if (ApiClient.Instance != null) ApiClient.Instance.OnResponseReceived -= OnApiResponse;
        if (GameManager.Instance != null) GameManager.Instance.OnGameOver -= OnDefeat;
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
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        SceneManager.LoadScene(mainMenuScene);
    }

    // ────────────────────────── VITÓRIA / DERROTA ──────────────────────────

    private void OnApiResponse(AnalyzeResponse response)
    {
        if (Ended != EndKind.None) return;
        if (response?.status_investigacao == null) return;

        // a derrota chega pelo OnGameOver do GameManager; aqui só a vitória
        if (response.status_investigacao.fim_de_jogo) return;

        if (response.id_turno >= victoryTurn &&
            response.status_investigacao.nivel_suspeita <= victoryMaxSuspicion)
        {
            EndRun(EndKind.Victory);
        }
    }

    private void OnDefeat() => EndRun(EndKind.Defeat);

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

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (endTitle != null)
            endTitle.text = kind == EndKind.Victory ? "LIBERADO" : "CASO ENCERRADO";

        if (endBody != null)
            endBody.text = kind == EndKind.Victory
                ? $"Você atravessou {victoryTurn} turnos sem se contradizer.\n" +
                  "O detetive não reuniu o suficiente para sustentar a acusação."
                : "A suspeita se manteve no limite por três turnos seguidos.\n" +
                  "O detetive fechou o caso contra você.";

        Show(endOverlay);
        Debug.Log($"[GameFlow] fim de jogo: {kind}");
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
