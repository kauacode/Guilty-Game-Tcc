using UnityEditor;
using UnityEngine;

/// <summary>
/// Ferramenta de setup da Fase 4 (câmera mouselook).
/// Encontra a Main Camera na cena e anexa PlayerCameraLook a ela,
/// caso ainda não esteja presente. Não salva a cena automaticamente.
/// </summary>
public static class GuiltySceneSetupCamera
{
    [MenuItem("Guilty/Fase 4 - Setup Câmera Mouselook")]
    public static void SetupCamera()
    {
        Camera mainCam = Camera.main;
        if (mainCam == null)
        {
            Debug.LogError("[GuiltySetup] Main Camera não encontrada. Certifique-se de que a câmera tem a tag 'MainCamera'.");
            return;
        }

        PlayerCameraLook look = mainCam.GetComponent<PlayerCameraLook>();
        if (look == null)
        {
            look = mainCam.gameObject.AddComponent<PlayerCameraLook>();
            Undo.RegisterCreatedObjectUndo(look, "Add PlayerCameraLook");
            Debug.Log($"[GuiltySetup] PlayerCameraLook adicionado à '{mainCam.gameObject.name}'.");
        }
        else
        {
            Debug.Log("[GuiltySetup] PlayerCameraLook já estava presente na Main Camera.");
        }

        Debug.Log("[GuiltySetup] Fase 4 concluída. Entre em Play, mova o mouse e verifique o clamping. Salve a cena com Ctrl+S.");
    }
}
