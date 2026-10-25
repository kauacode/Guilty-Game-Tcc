using UnityEngine;
using System;

/// <summary>
/// Coordena o estado global do jogo.
/// Não sabe nada sobre UI ou API — só mantém o estado.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Configuração da Sessão")]
    [SerializeField] private string sessionId;

    // Estado público somente leitura
    public string SessionId => sessionId;
    public int CurrentTurn { get; private set; } = 0;
    public int SuspicionLevel { get; private set; } = 0;
    public bool IsGameOver { get; private set; } = false;
    /// <summary>"vitoria" | "derrota" | "" — decidido pelo backend (regras em game_rules.py).</summary>
    public string Result { get; private set; } = "";
    /// <summary>suspeita_maxima | suspeita_sustentada | liberado | limite_de_turnos</summary>
    public string EndReason { get; private set; } = "";
    public int HighSuspicionStreak { get; private set; } = 0;

    /// <summary>
    /// Nome mostrado ao jogador. O session_id (player_xxxxxxxx) é identificador
    /// TÉCNICO — vai para o backend, nunca para a tela. O jogador interpreta o
    /// suspeito do caso, então o nome amigável é o do personagem.
    /// </summary>
    public const string DefaultPlayerName = "Daniel Reis";
    public string PlayerDisplayName =>
        string.IsNullOrEmpty(CaseSelection.PlayerName) ? DefaultPlayerName : CaseSelection.PlayerName;

    // Eventos de estado do jogo
    public event Action<int> OnSuspicionChanged;
    public event Action<bool> OnLieDetected;
    public event Action OnGameOver;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // sem o return, a duplicata ainda gerava um sessionId e logava
            // "Sessão iniciada" com um ID que morre no frame seguinte
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject); // Persiste entre cenas

        // Gera um ID único de sessão se não estiver definido
        if (string.IsNullOrEmpty(sessionId))
        {
            sessionId = NewSessionId();
        }

        Debug.Log($"[GameManager] Sessão iniciada: {sessionId}");
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private static string NewSessionId()
    {
        return $"player_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
    }

    public void ApplyTurnResult(AnalyzeResponse response)
    {
        if (IsGameOver) return;
        // Resposta de partida já encerrada no backend (reenvio): estado final,
        // nada a aplicar.
        if (response.ignorado) return;

        CurrentTurn = response.id_turno;
        HighSuspicionStreak = response.status_investigacao.turnos_suspeita_alta;

        int previousSuspicion = SuspicionLevel;
        SuspicionLevel = response.status_investigacao.nivel_suspeita;

        if (SuspicionLevel != previousSuspicion)
            OnSuspicionChanged?.Invoke(SuspicionLevel);

        if (response.status_investigacao.detectou_mentira)
            OnLieDetected?.Invoke(true);

        if (response.status_investigacao.fim_de_jogo)
        {
            IsGameOver = true;
            Result = response.status_investigacao.resultado ?? "";
            EndReason = response.status_investigacao.motivo_fim ?? "";
            OnGameOver?.Invoke();
        }
    }

    /// <summary>
    /// Encerra a partida atual e começa outra do zero. Cada partida é única:
    /// a sessão antiga é finalizada no backend (histórico apagado) e a nova
    /// ganha outro ID. Chamado por Reiniciar, Voltar ao menu e JOGAR.
    /// </summary>
    public void ResetGame()
    {
        // GameManager sobrevive à troca de cena, então a corrotina continua
        // mesmo com a cena sendo recarregada logo em seguida.
        if (!string.IsNullOrEmpty(sessionId))
            StartCoroutine(BackendApi.EndSession(sessionId));

        CurrentTurn = 0;
        SuspicionLevel = 0;
        IsGameOver = false;
        Result = "";
        EndReason = "";
        HighSuspicionStreak = 0;
        ApiClient.Instance?.ResetInteraction();
        // Gera novo ID de sessão para reset completo
        sessionId = NewSessionId();

        // Sem este evento, quem desenha a suspeita (barra da HUD e vinheta do
        // SuspicionVisualFeedback) ficava preso no valor do jogo anterior.
        OnSuspicionChanged?.Invoke(SuspicionLevel);

        Debug.Log($"[GameManager] Jogo resetado. Nova sessão: {sessionId}");
    }
}