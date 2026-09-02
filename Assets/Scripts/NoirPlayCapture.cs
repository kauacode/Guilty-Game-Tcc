using System.Collections;
using System.IO;
using UnityEngine;

// Código de VALIDAÇÃO, não de jogo. A classe inteira fica sob UNITY_EDITOR
// para não ser compilada no build: é um MonoBehaviour de teste que só faz
// sentido dirigido pelos scripts de Editor do piloto.
#if UNITY_EDITOR

/// <summary>
/// Captura de validação do filtro noir em Play mode — é onde a vinheta
/// (dirigida pelo SuspicionVisualFeedback) e o film grain realmente aparecem.
///
/// Tira duas fotos: uma logo no início e outra depois do fade da vinheta assentar.
/// Sai do Play mode sozinho no fim. Só existe para validação; o
/// GuiltyNoirPlaytest remove o objeto que hospeda este componente ao terminar.
/// </summary>
public class NoirPlayCapture : MonoBehaviour
{
    private IEnumerator Start()
    {
        var dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "PilotScreens");

        yield return new WaitForSeconds(0.4f);
        Shot(dir, "play_noir_inicio");

        // o fade da vinheta usa fadeSpeed = 2, então ~1s cobre o transiente
        yield return new WaitForSeconds(1.6f);
        Shot(dir, "play_noir_assentado");

        yield return new WaitForSeconds(0.6f);

        UnityEditor.EditorApplication.isPlaying = false;
    }

    private static void Shot(string dir, string name)
    {
        try
        {
            Directory.CreateDirectory(dir);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
            Debug.Log("[Noir] captura " + name);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[Noir] captura falhou: " + e.Message);
        }
    }
}
#endif
