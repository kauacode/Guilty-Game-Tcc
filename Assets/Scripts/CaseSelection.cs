/// <summary>
/// Caso escolhido na seleção de casos. Estático porque sobrevive à troca de
/// cena sem precisar de um objeto DontDestroyOnLoad só para guardar uma string.
/// Vazio = o backend usa o caso padrão.
/// </summary>
public static class CaseSelection
{
    public static string CaseId = "";
}
