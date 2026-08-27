using UnityEditor;
using UnityEngine;

/// <summary>
/// Ferramenta de setup da Fase 3 (feedback de suspeita no Global Volume).
/// Anexa o SuspicionVisualFeedback ao GameObject "Global Volume" já existente
/// na cena. Não salva a cena automaticamente — revise e salve com Ctrl+S.
/// </summary>
public static class GuiltySceneSetupFeedback
{
    [MenuItem("Guilty/Fase 3 - Setup Feedback de Suspeita")]
    public static void SetupFeedback()
    {
        GameObject volumeGO = GameObject.Find("Global Volume");
        if (volumeGO == null)
        {
            Debug.LogError("[GuiltySetup] GameObject 'Global Volume' não encontrado na cena.");
            return;
        }

        SuspicionVisualFeedback feedback = volumeGO.GetComponent<SuspicionVisualFeedback>();
        if (feedback == null)
        {
            feedback = volumeGO.AddComponent<SuspicionVisualFeedback>();
            Undo.RegisterCreatedObjectUndo(feedback, "Add SuspicionVisualFeedback");
            Debug.Log("[GuiltySetup] SuspicionVisualFeedback adicionado ao Global Volume.");
        }
        else
        {
            Debug.Log("[GuiltySetup] SuspicionVisualFeedback já estava presente no Global Volume.");
        }

        Debug.Log("[GuiltySetup] Fase 3 concluída. Rode o jogo (Play) para ver a Vignette reagir ao nível de suspeita, depois salve a cena.");
    }
}
