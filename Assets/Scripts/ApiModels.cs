using System;

// Esses objetos espelham exatamente o JSON que a sua API retorna.
// O Unity usará JsonUtility para deserializar automaticamente.
// Campos que o backend não enviar ficam no valor padrão (JsonUtility ignora
// o que falta), então versões antigas do backend continuam funcionando.

[Serializable]
public class AnalyzeRequest
{
    public string session_id;
    public string player_text;
    // IA escolhida em Configurações > Detetive (IA): "gemini" ou um modelo local.
    public string provider;
    public string gemini_api_key;
    // Caso escolhido na seleção de casos; vazio = caso padrão do backend.
    public string case_id;
    // Janela de resposta aberta (timer de 60 s). Vazio = sem regra de tempo.
    public string interacao_id;

    public AnalyzeRequest(string sessionId, string text, string aiProvider, string geminiApiKey,
                          string caseId = "", string interacaoId = "")
    {
        session_id = sessionId;
        player_text = text;
        provider = aiProvider;
        gemini_api_key = geminiApiKey;
        case_id = caseId;
        interacao_id = interacaoId;
    }
}

[Serializable]
public class StatusInvestigacao
{
    public int nivel_suspeita;
    public bool congelar_input;
    public bool detectou_mentira;
    public bool fim_de_jogo;
    // Preenchidos pelo backend quando fim_de_jogo = true.
    public string resultado;          // "vitoria" | "derrota"
    public string motivo_fim;         // suspeita_maxima | suspeita_sustentada | liberado | limite_de_turnos
    public int turnos_suspeita_alta;  // turnos seguidos com suspeita >= 80
    public int turnos_restantes;
}

[Serializable]
public class FeedbackVisual
{
    public string cor_iluminacao;
    public int bpm_musica;
    public string animacao_trigger;
}

[Serializable]
public class AnalyzeResponse
{
    public int id_turno;
    public string texto_detetive;
    public StatusInvestigacao status_investigacao;
    public FeedbackVisual feedback_visual;
    public string interacao_id;
    public bool timeout;              // true: o prazo acabou e a fala é do sistema
    public int penalidade_tempo;      // efeito da faixa de tempo (0 / +1 / +2)
    public int penalidade_timeout;    // só quando o prazo estourou
    public float tempo_resposta_s;
    public string nivel_tempo_resposta; // baixo | medio | alto | timeout
    public string frase_impaciencia;  // só no timeout
    public bool ignorado;             // partida já encerrada; nada foi alterado
    // Janela da PRÓXIMA resposta, já aberta pelo backend junto com esta fala.
    public string proxima_interacao_id;
    public float proxima_restante_s;
}

/// <summary>Janela de resposta devolvida por POST /session/{id}/interacao.</summary>
[Serializable]
public class ResponseWindow
{
    public string interacao_id;
    public float prazo_s;
    public float restante_s;
    public string estado;
}

[Serializable]
public class ExpireResult
{
    public bool aplicado;
    public AnalyzeResponse resposta;
}
