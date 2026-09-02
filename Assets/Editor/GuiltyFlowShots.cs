using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Screenshots das quatro telas novas, para conferir consistência visual.
///
/// Não entra em Play mode. Canvas em ScreenSpaceOverlay não aparece num
/// Camera.Render() para RenderTexture, então cada Canvas é trocado
/// temporariamente para ScreenSpaceCamera, renderizado, e devolvido ao estado
/// original. A cena NÃO é salva — nada disso persiste.
///
/// Rodar por: menu Guilty > Fluxo - Screenshots das Telas.
/// </summary>
[InitializeOnLoad]
public static class GuiltyFlowShots
{
    private const string GameScene = "Assets/Scenes/SampleScene.unity";
    private const string MenuScene = "Assets/Scenes/MainMenu.unity";
    private const int W = 1600, H = 900;

    private static string OutDir =>
        Path.Combine(Directory.GetParent(Application.dataPath).FullName, "PilotScreens");

    private static string MarkerPath =>
        Path.Combine(Directory.GetParent(Application.dataPath).FullName, ".guilty_flowshots_done");

    static GuiltyFlowShots()
    {
        if (File.Exists(MarkerPath)) return;
        EditorApplication.delayCall += () =>
        {
            if (File.Exists(MarkerPath)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MenuScene) == null) return;
            File.WriteAllText(MarkerPath, System.DateTime.Now.ToString("o"));
            try { Run(); }
            catch (System.Exception e) { Debug.LogError("[Shots] falhou: " + e); }
        };
    }

    [MenuItem("Guilty/Fluxo - Screenshots das Telas")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Directory.CreateDirectory(OutDir);

        // ── menu ──
        var menu = EditorSceneManager.OpenScene(MenuScene, OpenSceneMode.Single);
        Capture(menu, "flow_1_menu", () => ToggleSettings(menu, false));

        // ── configurações (painel existe fechado na cena do menu) ──
        Capture(menu, "flow_5_config", () => ToggleSettings(menu, true));
        ToggleSettings(menu, false);

        // ── pause e fim de jogo ──
        var game = EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
        var flow = Object.FindFirstObjectByType<GameFlowController>();
        if (flow == null) { Debug.LogError("[Shots] GameFlowController não encontrado."); return; }

        Capture(game, "flow_2_pause", () => Reveal(flow, "pauseOverlay"));
        Capture(game, "flow_3_vitoria", () =>
        {
            SetEndText(flow, "LIBERADO",
                "Você atravessou 8 turnos sem se contradizer.\n" +
                "O detetive não reuniu o suficiente para sustentar a acusação.");
            Reveal(flow, "endOverlay");
        });
        Capture(game, "flow_4_derrota", () =>
        {
            SetEndText(flow, "CASO ENCERRADO",
                "A suspeita se manteve no limite por três turnos seguidos.\n" +
                "O detetive fechou o caso contra você.");
            Reveal(flow, "endOverlay");
        });

        Debug.Log("[Shots] 4 telas capturadas em " + OutDir);
    }

    /// <summary>
    /// A tela de Configurações fica desativada na cena (o Awake que a esconde só
    /// roda em Play mode). Para fotografar, liga na mão e devolve depois.
    /// </summary>
    private static void ToggleSettings(Scene scene, bool on)
    {
        var panel = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<SettingsPanel>(true))
            .FirstOrDefault();
        if (panel == null) return;
        panel.gameObject.SetActive(on);
        var g = panel.GetComponent<CanvasGroup>();
        if (g != null) g.alpha = on ? 1f : 0f;
    }

    private static CanvasGroup Field(GameFlowController flow, string name)
    {
        var so = new SerializedObject(flow);
        return so.FindProperty(name).objectReferenceValue as CanvasGroup;
    }

    private static void Reveal(GameFlowController flow, string field)
    {
        // esconde os dois e mostra só o pedido
        foreach (var n in new[] { "pauseOverlay", "endOverlay" })
        {
            var g = Field(flow, n);
            if (g == null) continue;
            bool on = n == field;
            g.gameObject.SetActive(on);
            g.alpha = on ? 1f : 0f;
        }
    }

    private static void SetEndText(GameFlowController flow, string title, string body)
    {
        var so = new SerializedObject(flow);
        if (so.FindProperty("endTitle").objectReferenceValue is TMP_Text t) t.text = title;
        if (so.FindProperty("endBody").objectReferenceValue is TMP_Text b) b.text = body;
    }

    private static void Capture(Scene scene, string name, System.Action prepare)
    {
        prepare?.Invoke();

        var cam = Camera.main ?? Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
                                       .FirstOrDefault(c => c.enabled);
        if (cam == null) { Debug.LogError("[Shots] sem câmera em " + scene.name); return; }

        // ScreenSpaceOverlay não entra em RenderTexture — troca temporária
        var canvases = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Canvas>(true))
            .Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay)
            .ToArray();
        var before = canvases.Select(c => c.renderMode).ToArray();

        foreach (var c in canvases)
        {
            c.renderMode = RenderMode.ScreenSpaceCamera;
            c.worldCamera = cam;
            c.planeDistance = Mathf.Max(0.3f, cam.nearClipPlane + 0.2f);
        }
        Canvas.ForceUpdateCanvases();

        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
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

        File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());

        Object.DestroyImmediate(tex);
        rt.Release(); Object.DestroyImmediate(rt);

        for (int i = 0; i < canvases.Length; i++) canvases[i].renderMode = before[i];
        Canvas.ForceUpdateCanvases();

        Debug.Log("[Shots] " + name);
    }
}
