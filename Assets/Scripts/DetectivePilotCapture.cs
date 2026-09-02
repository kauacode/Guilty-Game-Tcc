using System.Collections;
using System.IO;
using UnityEngine;

// Código de VALIDAÇÃO, não de jogo. A classe inteira fica sob UNITY_EDITOR
// para não ser compilada no build: é um MonoBehaviour de teste que só faz
// sentido dirigido pelos scripts de Editor do piloto.
#if UNITY_EDITOR

/// <summary>
/// Captura de validação do piloto — roda em Play mode na cena AnimPilot_Detective.
///
/// Trava o Animator em normalized times que correspondem exatamente aos frames
/// que foram validados no Blender, tira screenshot de cada um e sai do Play mode.
/// Serve para comparar o resultado sob o shading real da Unity contra os
/// screenshots do Blender, frame a frame, sem depender de olho em movimento.
///
/// Só existe para o piloto. Some junto com o DetectiveIdleTrigger quando o
/// sistema definitivo de idles entrar.
/// </summary>
public class DetectivePilotCapture : MonoBehaviour
{
    private const string StateName = "FingerTap";
    private const int    ClipFrames = 48;   // 1..48; normalized = (f-1)/47

    // frame no Blender -> rótulo
    private static readonly (int frame, string label)[] Shots =
    {
        (1,  "f01_repouso"),
        (4,  "f04_antecipacao_pior_folga"),
        (11, "f11_pico1"),
        (14, "f14_impacto1"),
        (21, "f21_pico2"),
        (33, "f33_pico3"),
        (48, "f48_fim_loop"),
    };

    private void Start()
    {
        StartCoroutine(Capture());
    }

    private IEnumerator Capture()
    {
        var animator = GetComponent<Animator>();
        if (animator == null)
        {
            Debug.LogError("[Piloto] sem Animator no objeto " + name);
            yield break;
        }

        // o disparador provisório atrapalharia o controle manual do tempo
        var trigger = GetComponent<DetectiveIdleTrigger>();
        if (trigger != null) trigger.enabled = false;

        var dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "PilotScreens");
        Directory.CreateDirectory(dir);

        var lines = new System.Text.StringBuilder();
        int hash = Animator.StringToHash(StateName);

        yield return null;   // deixa o Animator inicializar

        foreach (var shot in Shots)
        {
            float t = (shot.frame - 1) / (float)(ClipFrames - 1);

            animator.Play(hash, 0, t);
            animator.Update(0f);
            yield return new WaitForEndOfFrame();

            Directory.CreateDirectory(dir);   // barato, e sobrevive a alguém apagar a pasta
            var file = Path.Combine(dir, "unity_" + shot.label + ".png");
            ScreenCapture.CaptureScreenshot(file);
            lines.AppendLine(shot.frame + "\t" + t.ToString("F4") + "\t" + Path.GetFileName(file));

            // CaptureScreenshot grava no fim do frame; dá uma folga antes do próximo
            yield return null;
            yield return null;
        }

        yield return new WaitForSeconds(0.5f);

        // shots.txt é o sinal de "terminei". Nada aqui pode derrubar a corrotina antes
        // do isPlaying = false, senão o editor fica preso em Play mode.
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "shots.txt"), lines.ToString());
            Debug.Log("[Piloto] screenshots em " + dir + "\n" + lines);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[Piloto] falhou ao gravar shots.txt: " + e.Message);
        }

        UnityEditor.EditorApplication.isPlaying = false;
    }
}
#endif
