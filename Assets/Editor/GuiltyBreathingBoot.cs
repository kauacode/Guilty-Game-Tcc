using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Dispara uma única vez a criação da layer aditiva de respiração.
///
/// Existe separado do GuiltyBreathingLayer porque a Unity nem sempre recompila
/// um arquivo editado sem foco; um arquivo NOVO é sempre importado e compilado.
/// Pode apagar depois que a layer estiver criada — o menu
/// "Guilty > Respiração - Criar Layer Aditiva" continua funcionando sozinho.
/// </summary>
[InitializeOnLoad]
public static class GuiltyBreathingBoot
{
    private static string MarkerPath =>
        Path.Combine(Directory.GetParent(Application.dataPath).FullName, ".guilty_breathing_done");

    static GuiltyBreathingBoot()
    {
        if (File.Exists(MarkerPath)) return;
        EditorApplication.delayCall += () =>
        {
            if (File.Exists(MarkerPath)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.WriteAllText(MarkerPath, System.DateTime.Now.ToString("o"));
            try { GuiltyBreathingLayer.Run(); }
            catch (System.Exception e) { Debug.LogError("[Respiração] boot falhou: " + e); }
        };
    }
}
