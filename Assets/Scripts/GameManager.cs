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

    // Eventos de estado do jogo
    public event Action<int> OnSuspicionChanged;
    public event Action<bool> OnLieDetected;
    public event Action OnGameOver;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // Persiste entre cenas
        }
        else
        {
            Destroy(gameObject);
        }

        // Gera um ID único de sessão se não estiver definido
        if (string.IsNullOrEmpty(sessionId))
        {
            sessionId = $"player_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
        }

        Debug.Log($"[GameManager] Sessão iniciada: {sessionId}");
    }

    public void ApplyTurnResult(AnalyzeResponse response)
    {
        if (IsGameOver) return;

        CurrentTurn = response.id_turno;

        int previousSuspicion = SuspicionLevel;
        SuspicionLevel = response.status_investigacao.nivel_suspeita;

        if (SuspicionLevel != previousSuspicion)
            OnSuspicionChanged?.Invoke(SuspicionLevel);

        if (response.status_investigacao.detectou_mentira)
            OnLieDetected?.Invoke(true);

        if (response.status_investigacao.fim_de_jogo)
        {
            IsGameOver = true;
            OnGameOver?.Invoke();
        }
    }

    public void ResetGame()
    {
        CurrentTurn = 0;
        SuspicionLevel = 0;
        IsGameOver = false;
        // Gera novo ID de sessão para reset completo
        sessionId = $"player_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
        Debug.Log($"[GameManager] Jogo resetado. Nova sessão: {sessionId}");
    }
}