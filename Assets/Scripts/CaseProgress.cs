using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Progresso do jogador entre sessões: quais casos já foram resolvidos.
/// Em PlayerPrefs (como o GameSettings) porque é só uma marca por caso — não
/// justifica um sistema de save. Quem decide se o caso foi resolvido é o
/// backend (Result == "vitoria"); aqui só se guarda o resultado.
/// </summary>
public static class CaseProgress
{
    private const string KeyPrefix = "guilty.solved.";

    /// <summary>Mesmo caso padrão do backend (case_repository.CASO_PADRAO).</summary>
    public const string DefaultCaseId = "CASO 01";

    public static bool IsSolved(string caseId)
    {
        if (string.IsNullOrEmpty(caseId)) return false;
        return PlayerPrefs.GetInt(KeyPrefix + caseId, 0) == 1;
    }

    /// <summary>
    /// Marca o caso como resolvido. Vazio = o caso padrão: é o que o backend
    /// jogou quando o Unity não mandou case_id. Vencer de novo não muda nada.
    /// </summary>
    public static void MarkSolved(string caseId)
    {
        if (string.IsNullOrEmpty(caseId)) caseId = DefaultCaseId;
        if (IsSolved(caseId)) return;
        PlayerPrefs.SetInt(KeyPrefix + caseId, 1);
        // Save na hora: se o jogo fechar na tela de fim, o progresso não se perde.
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Estado efetivo do cadeado. O isLocked da cena é o "bloqueado por padrão";
    /// um caso bloqueado abre quando o caso imediatamente anterior (pela ordem
    /// dos caseId: CASO 01 &lt; CASO 02 &lt; CASO 03) está resolvido. O primeiro
    /// caso não tem anterior, então segue só a cena.
    /// </summary>
    public static bool IsLocked(string caseId, bool lockedByDefault, IEnumerable<string> allCaseIds)
    {
        if (!lockedByDefault) return false;
        string previous = PreviousCaseId(caseId, allCaseIds);
        return previous == null || !IsSolved(previous);
    }

    /// <summary>
    /// O caso imediatamente anterior pela ordem dos caseId, ou null se for o
    /// primeiro. É o que precisa ser vencido para este abrir.
    /// </summary>
    public static string PreviousCaseId(string caseId, IEnumerable<string> allCaseIds)
    {
        string previous = null;
        foreach (var id in allCaseIds)
        {
            if (string.IsNullOrEmpty(id) || string.CompareOrdinal(id, caseId) >= 0) continue;
            if (previous == null || string.CompareOrdinal(id, previous) > 0) previous = id;
        }
        return previous;
    }
}
