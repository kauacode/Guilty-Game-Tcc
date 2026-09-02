using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static GuiltyNoirUI;

/// <summary>
/// Monta as telas que faltavam: menu inicial (cena própria) e os overlays de
/// pause e fim de jogo (dentro da SampleScene).
///
/// Tudo passa pelo GuiltyNoirUI, então as quatro telas herdam a mesma paleta,
/// tipografia e geometria — consistência por construção, não por disciplina.
///
/// Não encosta na HUD existente (barra de suspeita, prompt do TAB): os overlays
/// são Canvas separados, por cima.
///
/// Rodar por: menu Guilty > Fluxo - Montar Menu, Pause e Fim de Jogo.
/// </summary>
[InitializeOnLoad]
public static class GuiltyGameFlowUI
{
    private const string GameScenePath = "Assets/Scenes/SampleScene.unity";
    private const string MenuScenePath = "Assets/Scenes/MainMenu.unity";

    private static string MarkerPath =>
        Path.Combine(Directory.GetParent(Application.dataPath).FullName, ".guilty_gameflow_done");

    static GuiltyGameFlowUI()
    {
        if (File.Exists(MarkerPath)) return;
        EditorApplication.delayCall += () =>
        {
            if (File.Exists(MarkerPath)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.WriteAllText(MarkerPath, System.DateTime.Now.ToString("o"));
            try { Run(); }
            catch (System.Exception e) { Debug.LogError("[Fluxo] auto-setup falhou: " + e); }
        };
    }

    [MenuItem("Guilty/Fluxo - Montar Menu, Pause e Fim de Jogo")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Fluxo] saia do Play mode antes.");
            return;
        }

        var log = new List<string>();
        BuildMenuScene(log);
        BuildInGameOverlays(log);
        RegisterScenes(log);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Fluxo] pronto.\n  " + string.Join("\n  ", log));
    }

    // ─────────────────────────────── MENU ───────────────────────────────

    private static void BuildMenuScene(List<string> log)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

        var camGo = new GameObject("Menu Camera", typeof(Camera));
        SceneManager.MoveGameObjectToScene(camGo, scene);
        var cam = camGo.GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Ink;
        cam.orthographic = true;
        camGo.tag = "MainCamera";

        var canvas = MakeCanvas("Menu Canvas", scene, out var canvasGo);
        var root = canvasGo.AddComponent<CanvasGroup>();

        FullScreen("Backdrop", canvasGo.transform, Ink);

        // bloco de conteúdo alinhado à esquerda — leitura de dossiê, não de pôster
        var block = new GameObject("Content", typeof(RectTransform));
        block.transform.SetParent(canvasGo.transform, false);
        var br = Rect(block);
        br.anchorMin = new Vector2(0f, 0.5f); br.anchorMax = new Vector2(0f, 0.5f);
        br.pivot = new Vector2(0f, 0.5f);
        br.anchoredPosition = new Vector2(190f, 40f);
        br.sizeDelta = new Vector2(900f, 600f);

        var title = Text("Title", block.transform, "GUILTY", TitleSize, TextHi, TitleSpacing);
        Place(title.rectTransform, new Vector2(0f, 210f), new Vector2(900f, 84f));

        var rule = Rule("TitleRule", block.transform, 132f, 3f, Amber);
        PlaceRect(Rect(rule), new Vector2(4f, 168f));

        var sub = Text("Subtitle", block.transform,
                       "SALA DE INTERROGATÓRIO   ·   DEPARTAMENTO DE POLÍCIA",
                       SubtitleSize, TextMuted, LabelSpacing);
        Place(sub.rectTransform, new Vector2(0f, 128f), new Vector2(900f, 26f));

        var col = Column("Buttons", block.transform, 14f);
        PlaceRect(Rect(col), new Vector2(0f, 40f));
        Rect(col).pivot = new Vector2(0f, 1f);

        var size = new Vector2(360f, 56f);
        var startBtn  = MenuButton("Btn_Start",    col.transform, "Iniciar interrogatório", size);
        var configBtn = MenuButton("Btn_Config",   col.transform, "Configurações", size, enabled: false);
        var quitBtn   = MenuButton("Btn_Quit",     col.transform, "Sair", size);

        // abaixo da coluna (3 botões de 56 + 2 espaços de 14 = 196px a partir de y=40)
        var hint = Text("ConfigHint", block.transform,
                        "Configurações ainda não implementadas.",
                        14f, new Color(TextMuted.r, TextMuted.g, TextMuted.b, 0.65f), 6f);
        var hr = hint.rectTransform;
        hr.anchorMin = hr.anchorMax = new Vector2(0f, 0.5f);
        hr.pivot = new Vector2(0f, 0.5f);
        hr.anchoredPosition = new Vector2(4f, -186f);
        hr.sizeDelta = new Vector2(560f, 20f);

        var foot = Text("Footer", canvasGo.transform,
                        "PROTÓTIPO ACADÊMICO   ·   DESIGN DE JOGOS DIGITAIS",
                        13f, new Color(TextMuted.r, TextMuted.g, TextMuted.b, 0.55f), 8f);
        var fr = foot.rectTransform;
        fr.anchorMin = new Vector2(0f, 0f); fr.anchorMax = new Vector2(0f, 0f);
        fr.pivot = new Vector2(0f, 0f);
        fr.anchoredPosition = new Vector2(190f, 56f);
        fr.sizeDelta = new Vector2(700f, 20f);

        var ctrl = canvasGo.AddComponent<MainMenuController>();
        var so = new SerializedObject(ctrl);
        so.FindProperty("root").objectReferenceValue = root;
        so.FindProperty("gameScene").stringValue = "SampleScene";
        so.ApplyModifiedPropertiesWithoutUndo();

        Wire(startBtn, ctrl, "StartGame");
        Wire(quitBtn,  ctrl, "QuitGame");

        MakeEventSystem(scene);

        EditorSceneManager.SaveScene(scene, MenuScenePath);
        EditorSceneManager.CloseScene(scene, true);
        log.Add("cena de menu: " + MenuScenePath);
    }

    // ───────────────────────── PAUSE + FIM DE JOGO ─────────────────────────

    private static void BuildInGameOverlays(List<string> log)
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != GameScenePath)
            scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);

        // Canvas próprio, acima da HUD, para não encostar no Canvas existente
        var canvas = MakeCanvas("Flow Canvas", scene, out var canvasGo);
        canvas.sortingOrder = 100;

        var pause = BuildPause(canvasGo.transform, out var resumeBtn, out var restartBtn, out var menuBtn);
        var end   = BuildEnd(canvasGo.transform, out var againBtn, out var toMenuBtn,
                             out var endTitle, out var endBody);

        var flow = canvasGo.GetComponent<GameFlowController>() ?? canvasGo.AddComponent<GameFlowController>();
        var so = new SerializedObject(flow);
        so.FindProperty("pauseOverlay").objectReferenceValue = pause;
        so.FindProperty("endOverlay").objectReferenceValue   = end;
        so.FindProperty("endTitle").objectReferenceValue     = endTitle;
        so.FindProperty("endBody").objectReferenceValue      = endBody;
        so.FindProperty("mainMenuScene").stringValue         = "MainMenu";
        so.ApplyModifiedPropertiesWithoutUndo();

        Wire(resumeBtn,  flow, "Resume");
        Wire(restartBtn, flow, "RestartRun");
        Wire(menuBtn,    flow, "QuitToMenu");
        Wire(againBtn,   flow, "RestartRun");
        Wire(toMenuBtn,  flow, "QuitToMenu");

        MakeEventSystem(scene);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.Add("overlays de pause e fim de jogo na SampleScene");
    }

    private static CanvasGroup BuildPause(Transform parent, out Button resume, out Button restart, out Button menu)
    {
        var go = FullScreen("Pause Overlay", parent, Scrim);
        var g = go.AddComponent<CanvasGroup>();

        var block = new GameObject("Content", typeof(RectTransform));
        block.transform.SetParent(go.transform, false);
        var br = Rect(block);
        br.anchorMin = br.anchorMax = new Vector2(0.5f, 0.5f);
        br.pivot = new Vector2(0.5f, 0.5f);
        br.sizeDelta = new Vector2(760f, 460f);

        var t = Text("Title", block.transform, "PAUSADO", 44f, TextHi, TitleSpacing,
                     TextAlignmentOptions.Center);
        Place(t.rectTransform, new Vector2(0f, 150f), new Vector2(760f, 60f));

        var rule = Rule("Rule", block.transform, 96f, 2f, Amber);
        var rr = Rect(rule);
        rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 0.5f);
        rr.pivot = new Vector2(0.5f, 0.5f);
        rr.anchoredPosition = new Vector2(0f, 116f);

        var col = Column("Buttons", block.transform, 12f);
        var cr = Rect(col);
        cr.anchorMin = cr.anchorMax = new Vector2(0.5f, 0.5f);
        cr.pivot = new Vector2(0.5f, 1f);
        cr.anchoredPosition = new Vector2(0f, 60f);

        var size = new Vector2(360f, 54f);
        // rótulos curtos: com letter-spacing, "Reiniciar interrogatório" quebrava
        // em duas linhas e estourava a altura do botão
        resume  = MenuButton("Btn_Resume",  col.transform, "Continuar", size);
        restart = MenuButton("Btn_Restart", col.transform, "Reiniciar", size);
        menu    = MenuButton("Btn_Menu",    col.transform, "Sair para o menu", size);

        var hint = Text("Hint", block.transform, "ESC PARA CONTINUAR", 13f,
                        new Color(TextMuted.r, TextMuted.g, TextMuted.b, 0.7f), 10f,
                        TextAlignmentOptions.Center);
        Place(hint.rectTransform, new Vector2(0f, -170f), new Vector2(760f, 20f));

        return g;
    }

    private static CanvasGroup BuildEnd(Transform parent, out Button again, out Button toMenu,
                                        out TMP_Text title, out TMP_Text body)
    {
        var go = FullScreen("End Overlay", parent, new Color(Ink.r, Ink.g, Ink.b, 0.96f));
        var g = go.AddComponent<CanvasGroup>();

        var block = new GameObject("Content", typeof(RectTransform));
        block.transform.SetParent(go.transform, false);
        var br = Rect(block);
        br.anchorMin = br.anchorMax = new Vector2(0.5f, 0.5f);
        br.pivot = new Vector2(0.5f, 0.5f);
        br.sizeDelta = new Vector2(860f, 520f);

        title = Text("EndTitle", block.transform, "CASO ENCERRADO", 52f, TextHi, TitleSpacing,
                     TextAlignmentOptions.Center);
        Place(title.rectTransform, new Vector2(0f, 168f), new Vector2(860f, 70f));

        var rule = Rule("Rule", block.transform, 120f, 3f, Amber);
        var rr = Rect(rule);
        rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 0.5f);
        rr.pivot = new Vector2(0.5f, 0.5f);
        rr.anchoredPosition = new Vector2(0f, 128f);

        body = Text("EndBody", block.transform, "", BodySize, TextMuted, 4f,
                    TextAlignmentOptions.Center);
        Place(body.rectTransform, new Vector2(0f, 56f), new Vector2(720f, 90f));

        var col = Column("Buttons", block.transform, 12f);
        var cr = Rect(col);
        cr.anchorMin = cr.anchorMax = new Vector2(0.5f, 0.5f);
        cr.pivot = new Vector2(0.5f, 1f);
        cr.anchoredPosition = new Vector2(0f, -20f);

        var size = new Vector2(360f, 54f);
        again  = MenuButton("Btn_Again",  col.transform, "Jogar novamente", size);
        toMenu = MenuButton("Btn_ToMenu", col.transform, "Voltar ao menu", size);

        return g;
    }

    // ─────────────────────────────── apoio ───────────────────────────────

    private static Canvas MakeCanvas(string name, Scene scene, out GameObject go)
    {
        go = new GameObject(name, typeof(RectTransform), typeof(Canvas),
                            typeof(CanvasScaler), typeof(GraphicRaycaster));
        SceneManager.MoveGameObjectToScene(go, scene);
        var c = go.GetComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        var s = go.GetComponent<CanvasScaler>();
        s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        s.referenceResolution = new Vector2(1920f, 1080f);   // mesma do GuiltySceneSetupUI
        s.matchWidthOrHeight = 0.5f;
        return c;
    }

    /// <summary>
    /// O projeto está em activeInputHandler = 1 (só Input System novo). Um
    /// EventSystem com StandaloneInputModule não recebe clique nenhum nesse
    /// modo — tem que ser InputSystemUIInputModule.
    /// </summary>
    private static void MakeEventSystem(Scene scene)
    {
        var existing = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<EventSystem>(true)).FirstOrDefault();
        if (existing != null)
        {
            if (existing.GetComponent<InputSystemUIInputModule>() == null)
            {
                var legacy = existing.GetComponent<BaseInputModule>();
                if (legacy != null) Object.DestroyImmediate(legacy);
                existing.gameObject.AddComponent<InputSystemUIInputModule>();
            }
            return;
        }
        var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        SceneManager.MoveGameObjectToScene(go, scene);
    }

    private static void Wire(Button btn, Object target, string method)
    {
        if (btn == null) return;
        for (int i = btn.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
            UnityEditor.Events.UnityEventTools.RemovePersistentListener(btn.onClick, i);
        var call = System.Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction),
                                                  target, method) as UnityEngine.Events.UnityAction;
        UnityEditor.Events.UnityEventTools.AddPersistentListener(btn.onClick, call);
    }

    private static void Place(RectTransform r, Vector2 pos, Vector2 size)
    {
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = pos;
        r.sizeDelta = size;
    }

    private static void PlaceRect(RectTransform r, Vector2 pos)
    {
        r.anchorMin = r.anchorMax = new Vector2(0f, 0.5f);
        r.pivot = new Vector2(0f, 0.5f);
        r.anchoredPosition = pos;
    }

    private static void RegisterScenes(List<string> log)
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        bool hasMenu = scenes.Any(s => s.path == MenuScenePath);
        if (!hasMenu)
        {
            // menu primeiro: é a cena que abre o jogo
            scenes.Insert(0, new EditorBuildSettingsScene(MenuScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            log.Add("MainMenu adicionada ao Build Settings como cena 0");
        }
        else log.Add("MainMenu já estava no Build Settings");
    }
}
