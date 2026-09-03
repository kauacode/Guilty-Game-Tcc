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
}
