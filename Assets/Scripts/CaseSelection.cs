/// <summary>
/// Caso escolhido na seleção de casos. Estático porque sobrevive à troca de
/// cena sem precisar de um objeto DontDestroyOnLoad só para guardar uma string.
/// Vazio = o backend usa o caso padrão.
/// </summary>
public static class CaseSelection
{
    public static string CaseId = "";
    /// <summary>Nome do suspeito do caso. Vazio = GameManager.DefaultPlayerName.</summary>
    public static string PlayerName = "";
    /// <summary>Fala de abertura do caso. Vazio = UIController.openingLine.</summary>
    public static string OpeningLine = "";
    /// <summary>Quem o jogador é, no tutorial. Vazio = apresentação do Daniel Reis.</summary>
    public static string PlayerIntro = "";
}
