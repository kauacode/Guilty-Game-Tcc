using System;

// Esses objetos espelham exatamente o JSON que a sua API retorna.
// O Unity usará JsonUtility para deserializar automaticamente.

[Serializable]
public class AnalyzeRequest
{
    public string session_id;
    public string player_text;
    public string mode;

    public AnalyzeRequest(string sessionId, string text, string apiMode = "mock")
    {
        session_id = sessionId;
        player_text = text;
        mode = apiMode;
    }
}

[Serializable]
public class StatusInvestigacao
{
    public int nivel_suspeita;
    public bool congelar_input;
    public bool detectou_mentira;
    public bool fim_de_jogo;
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
}