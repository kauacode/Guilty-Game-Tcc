using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Renderiza a câmera principal da cena aberta para um PNG, em edit mode.
/// Serve para conferir a cena do jogo sem entrar em Play mode.
///
/// Rodar por: menu Guilty > Piloto - Screenshot da Cena.
/// </summary>
// Só pelo menu. O auto-run (InitializeOnLoad + marcador .guilty_*_done) foi
// tirado: o marcador não vai para o Git, então todo clone novo tirava o
// screenshot sozinho ao abrir o projeto.
public static class GuiltySceneShot
{
    [MenuItem("Guilty/Piloto - Screenshot da Cena")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        var scene = EditorSceneManager.GetActiveScene();
        var cam = Camera.main;
        if (cam == null)
        {
            foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            { if (c.enabled) { cam = c; break; } }
        }
        if (cam == null) { Debug.LogError("[Piloto] nenhuma câmera na cena " + scene.name); return; }

        const int W = 1600, H = 900;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 2;

        var prevTarget = cam.targetTexture;
        var prevActive = RenderTexture.active;

        cam.targetTexture = rt;
        cam.Render();

        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();

        cam.targetTexture = prevTarget;
        RenderTexture.active = prevActive;

        var dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "PilotScreens");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "scene_" + scene.name + ".png");
        File.WriteAllBytes(file, tex.EncodeToPNG());

        Object.DestroyImmediate(tex);
        rt.Release();
        Object.DestroyImmediate(rt);

        Debug.Log("[Piloto] screenshot da cena " + scene.name + " (câmera " + cam.name + ") em " + file);
    }
}
