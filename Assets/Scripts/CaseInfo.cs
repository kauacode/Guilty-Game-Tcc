using UnityEngine;

[System.Serializable]
public class CaseInfo
{
    public string caseId          = "CASO 01";
    public string caseTitle       = "O Homicídio da Rua 7";
    public string statusBadge     = "AGUARDANDO INTERROGATÓRIO";
    [TextArea(4, 8)]
    public string caseDescription = "Insira a descrição completa do crime e anotações do investigador aqui...";
    public Sprite suspectPhoto;
    public string targetSceneName = "SampleScene";
    public bool   isLocked        = false;

    // Textos do personagem e da 1a pergunta, por caso. Vazios = os padrões do
    // CASO 01 (GameManager.DefaultPlayerName e UIController.openingLine). Devem
    // bater com papel_do_jogador.nome e abertura.pergunta do JSON do backend.
    [Tooltip("Nome do suspeito na HUD. Vazio = GameManager.DefaultPlayerName.")]
    public string playerName      = "";
    [TextArea(3, 6)]
    [Tooltip("Fala de abertura do detetive. Vazio = UIController.openingLine.")]
    public string openingLine     = "";
    [TextArea(2, 4)]
    [Tooltip("Quem o jogador é, no início do tutorial (aceita <b>). Vazio = apresentação do Daniel Reis.")]
    public string playerIntro     = "";
}
