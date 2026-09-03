using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static GuiltyNoirUI;

/// <summary>
/// Reconstrói a cena MainMenu com a sala 3D como plano de fundo e layout
/// de botões horizontais na base (referência visual: Dispatch).
///
/// Preserva 100% da lógica do MainMenuController (StartGame → CaseSelectionScene,
/// OpenSettings → PF_SettingsPanel, QuitGame). Substitui apenas o visual.
///
/// Rodar por: menu Guilty › Menu Definitivo - Reconstruir
/// Ação é idempotente: apaga tudo na cena e monta do zero.
/// </summary>
public static class GuiltyMainMenuDefinitive
{
    private const string MenuScenePath       = "Assets/Scenes/MainMenu.unity";
    private const string RoomFbxPath         = "Assets/Models/Environment/GUILTY_InterrogationRoom.fbx";
    private const string NoirVolumePath      = "Assets/Prefabs/PF_NoirVolume.prefab";
    private const string SettingsPanelPath   = "Assets/Prefabs/UI/PF_SettingsPanel.prefab";
    private const string DetectivePrefabPath = "Assets/Prefabs/Characters/PF_Detective.prefab";
    private const string LogoPath            = "Assets/UI/GUILTY_Logo.png";

    // Cor de destaque do menu: vermelho criminal em vez do âmbar compartilhado
    private static readonly Color MenuAccent = new Color(0.75f, 0.08f, 0.08f, 1f);

    [MenuItem("Guilty/Menu Definitivo - Reconstruir")]
    public static void Rebuild()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[MenuDef] Saia do Play mode antes de reconstruir.");
            return;
        }

        var scene = EditorSceneManager.OpenScene(MenuScenePath, OpenSceneMode.Single);
        ClearScene(scene);

        var room     = InstantiateRoom(scene);
        CreateCamera(scene, room);
        CreateLights(scene, room);
        PlaceDetective(scene, room);
        InstantiateVolume(scene);

        var canvasGo      = MakeCanvas(scene, out var root);
        var settingsGo    = InstantiateSettingsPanel(canvasGo.transform);
        BuildUI(canvasGo, canvasGo.transform, root, settingsGo);
        MakeEventSystem(scene);

        EnsureInBuildSettings();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.Refresh();

        Debug.Log("[MenuDef] Menu definitivo construído e salvo em " + MenuScenePath);
    }

    // ─────────────────── limpeza ───────────────────────────────────────────

    private static void ClearScene(Scene scene)
    {
        foreach (var go in scene.GetRootGameObjects())
            Object.DestroyImmediate(go);
    }

    // ─────────────────── 3D world ──────────────────────────────────────────

    private static GameObject InstantiateRoom(Scene scene)
    {
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(RoomFbxPath);
        if (fbx == null)
        {
            Debug.LogError("[MenuDef] FBX não encontrado: " + RoomFbxPath);
            var dummy = new GameObject("GUILTY_InterrogationRoom_MISSING");
            SceneManager.MoveGameObjectToScene(dummy, scene);
            return dummy;
        }

        var room = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        room.name = "GUILTY_InterrogationRoom";
        room.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        SceneManager.MoveGameObjectToScene(room, scene);
        Undo.RegisterCreatedObjectUndo(room, "Instantiate Interrogation Room");

        // Desativa o mesh estático do detetive no FBX da sala (sem animação, ficaria deformado)
        foreach (Transform t in room.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Detective_Posed" || t.name == "Detective")
                t.gameObject.SetActive(false);
        }

        return room;
    }

    private static void CreateCamera(Scene scene, GameObject room)
    {
        var go = new GameObject("Menu Camera", typeof(Camera));
        SceneManager.MoveGameObjectToScene(go, scene);
        go.tag = "MainCamera";
        Undo.RegisterCreatedObjectUndo(go, "Create Menu Camera");

        var cam = go.GetComponent<Camera>();
        cam.clearFlags      = CameraClearFlags.SolidColor;
        cam.backgroundColor = Ink;
        cam.orthographic    = false;
        cam.fieldOfView     = 50f;
        cam.nearClipPlane   = 0.1f;
        cam.farClipPlane    = 30f;

        // Posiciona a câmera usando as cadeiras como referência (igual ao GuiltySceneSetup3D).
        // Ângulo diferente: visão cinematic do "canto", não o POV do suspeito.
        Transform suspect  = FindDeepChild(room.transform, "PFB_Chair_Suspect");
        Transform detective = FindDeepChild(room.transform, "PFB_Chair_Detective");

        Vector3 focus, position;

        if (suspect != null && detective != null)
        {
            // ponto focal: 55 % em direção ao detetive, na altura do rosto
            focus = Vector3.Lerp(suspect.position, detective.position, 0.55f)
                  + Vector3.up * 0.9f;

            // câmera: atrás e ao lado do suspeito, elevada — compõe o ambiente
            Vector3 axis = (detective.position - suspect.position).normalized;
            Vector3 side = Vector3.Cross(axis, Vector3.up).normalized;
            position = suspect.position
                     - axis * 0.8f      // levemente atrás da cadeira do suspeito
                     + side * 1.0f      // deslocada para o lado
                     + Vector3.up * 2.2f;
        }
        else
        {
            // fallback para salas de tamanho padrão (~5 m de comprimento)
            focus    = new Vector3(0f, 0.8f, 0.3f);
            position = new Vector3(1.2f, 2.2f, -2.8f);
            Debug.LogWarning("[MenuDef] Cadeiras PFB_Chair_Suspect / PFB_Chair_Detective não " +
                             "encontradas no FBX. Câmera em posição padrão — ajuste no Inspector.");
        }

        go.transform.position = position;
        go.transform.LookAt(focus);
        // Dutch angle: leve inclinação psicológica (noir clássico)
        go.transform.Rotate(0f, 0f, 2.8f, Space.Self);

        // SMAA elimina o serrilhado (funciona em Deferred, diferente do MSAA)
        var camData = go.GetComponent<UniversalAdditionalCameraData>()
                   ?? go.AddComponent<UniversalAdditionalCameraData>();
        camData.antialiasing        = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        camData.antialiasingQuality = AntialiasingQuality.High;
        camData.renderPostProcessing = true;
    }

    private static void CreateLights(Scene scene, GameObject room)
    {
        Transform lamp = FindDeepChild(room.transform, "PFB_Prop_Lamp");
        Vector3 lampPos = lamp != null ? lamp.position : new Vector3(0f, 2.9f, 0f);

        // Spot principal — âmbar quente sobre a mesa de interrogatório
        AddLight(scene, "Light_Interrogation_Main", LightType.Spot,
                 lampPos, Quaternion.Euler(90f, 0f, 0f),
                 new Color(1f, 0.75f, 0.45f), intensity: 10f, range: 7f, spotAngle: 52f);

        // Preenchimento frio do lado do detetive
        Transform detective = FindDeepChild(room.transform, "PFB_Chair_Detective");
        Vector3 fillPos = detective != null
            ? detective.position + Vector3.up * 2.2f
            : new Vector3(0f, 2.2f, 1.5f);

        AddLight(scene, "Light_Fill_Detective", LightType.Point,
                 fillPos, Quaternion.identity,
                 new Color(0.45f, 0.65f, 0.85f), intensity: 2.5f, range: 5f, spotAngle: 0f);

        // Preenchimento geral — revela a geometria da sala sem destruir o noir
        AddLight(scene, "Light_Environment_Fill", LightType.Point,
                 new Vector3(0f, 2.7f, 0f), Quaternion.identity,
                 new Color(0.55f, 0.55f, 0.60f), intensity: 1.2f, range: 10f, spotAngle: 0f);

        // Luz vermelha que pisca ocasionalmente — tensão e mistério
        AddRedFlickerLight(scene, room);
    }

    private static void AddRedFlickerLight(Scene scene, GameObject room)
    {
        Transform suspect = FindDeepChild(room.transform, "PFB_Chair_Suspect");
        Vector3 cornerPos = suspect != null
            ? suspect.position + Vector3.up * 2.9f + new Vector3(-1.5f, 0f, -1.5f)
            : new Vector3(-1.5f, 2.9f, -1.5f);

        var go = new GameObject("Light_RedAmbient_Flicker", typeof(Light));
        SceneManager.MoveGameObjectToScene(go, scene);
        Undo.RegisterCreatedObjectUndo(go, "Create Red Flicker Light");
        go.transform.position = cornerPos;

        var l = go.GetComponent<Light>();
        l.type      = LightType.Point;
        l.color     = new Color(1f, 0.03f, 0.03f);
        l.intensity = 1.4f;   // base visível entre pulsos — sala sempre banhada de vermelho
        l.range     = 12f;

        go.AddComponent<MenuAmbientRedFlicker>();
    }

    private static void PlaceDetective(Scene scene, GameObject room)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DetectivePrefabPath);
        if (prefab == null)
        {
            Debug.LogWarning("[MenuDef] PF_Detective não encontrado — mesh estático já foi desativado.");
            return;
        }

        var det = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        det.name = "PF_Detective";
        det.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        Undo.RegisterCreatedObjectUndo(det, "Instantiate PF_Detective");

        // Remove DetectivePilotCapture: encerra o Play mode automaticamente (é da cena piloto)
        foreach (var comp in det.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (comp != null && comp.GetType().Name == "DetectivePilotCapture")
            {
                Object.DestroyImmediate(comp, true);
                Debug.Log("[MenuDef] DetectivePilotCapture removido do detetive no menu.");
            }
        }
    }

    private static void AddLight(Scene scene, string name, LightType type,
                                 Vector3 pos, Quaternion rot, Color color,
                                 float intensity, float range, float spotAngle)
    {
        var go = new GameObject(name, typeof(Light));
        SceneManager.MoveGameObjectToScene(go, scene);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);

        go.transform.position = pos;
        go.transform.rotation = rot;

        var l = go.GetComponent<Light>();
        l.type      = type;
        l.color     = color;
        l.intensity = intensity;
        l.range     = range;
        if (type == LightType.Spot) l.spotAngle = spotAngle;
    }

    private static void InstantiateVolume(Scene scene)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NoirVolumePath);
        if (prefab == null)
        {
            Debug.LogWarning("[MenuDef] PF_NoirVolume não encontrado em " + NoirVolumePath +
                             ". Execute Guilty > Noir - Aplicar Filtro antes deste passo.");
            return;
        }

        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.name = "NoirVolume";
        SceneManager.MoveGameObjectToScene(go, scene);
        Undo.RegisterCreatedObjectUndo(go, "Instantiate NoirVolume");
    }

    // ─────────────────── Canvas ────────────────────────────────────────────

    private static GameObject MakeCanvas(Scene scene, out CanvasGroup root)
    {
        var go = new GameObject("Menu Canvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        SceneManager.MoveGameObjectToScene(go, scene);
        Undo.RegisterCreatedObjectUndo(go, "Create Menu Canvas");

        var c = go.GetComponent<Canvas>();
        c.renderMode   = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 0;

        var s = go.GetComponent<CanvasScaler>();
        s.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        s.referenceResolution = new Vector2(1920f, 1080f);
        s.matchWidthOrHeight  = 0.5f;

        // CanvasGroup na raiz: controla o fade-in/out do MainMenuController
        root = go.AddComponent<CanvasGroup>();
        return go;
    }

    private static GameObject InstantiateSettingsPanel(Transform parent)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SettingsPanelPath);
        if (prefab == null)
        {
            Debug.LogWarning("[MenuDef] PF_SettingsPanel não encontrado em " + SettingsPanelPath);
            return null;
        }

        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.name = "SettingsPanel";
        Undo.RegisterCreatedObjectUndo(go, "Instantiate SettingsPanel");
        return go;
    }

    // ─────────────────── UI ────────────────────────────────────────────────

    private static void BuildUI(GameObject canvasGo, Transform canvas,
                                CanvasGroup root, GameObject settingsGo)
    {
        // ── Gradiente escuro na base (legibilidade dos botões sobre a cena 3D) ──
        var grad = Panel("BottomGradient", canvas, new Color(Ink.r, Ink.g, Ink.b, 0.88f));
        var gr = Rect(grad);
        gr.anchorMin = new Vector2(0f, 0f);
        gr.anchorMax = new Vector2(1f, 0f);
        gr.pivot     = new Vector2(0.5f, 0f);
        gr.sizeDelta = new Vector2(0f, 260f);

        // ── Logo — canto superior esquerdo ──
        var logoSprite = AssetDatabase.LoadAssetAtPath<Sprite>(LogoPath);
        if (logoSprite == null)
            Debug.LogWarning("[MenuDef] Logo não encontrado em " + LogoPath +
                             ". Reimporte o asset no Unity antes de reconstruir.");

        // PNG é 669×373 (ratio 1.7936). Rect ajustado ao ratio exato → sem letterboxing,
        // centro geométrico = centro visual, tagline alinha perfeitamente.
        const float logoW = 480f;
        const float logoH = 268f;  // 480 / 1.7936

        var logoGo = new GameObject("Logo", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        logoGo.transform.SetParent(canvas, false);
        var logoImg = logoGo.GetComponent<Image>();
        logoImg.sprite         = logoSprite;
        logoImg.preserveAspect = false;  // rect já tem o ratio correto
        logoImg.raycastTarget  = false;
        logoImg.color          = Color.white;
        var logoR = Rect(logoGo);
        logoR.anchorMin        = new Vector2(0f, 1f);
        logoR.anchorMax        = new Vector2(0f, 1f);
        logoR.pivot            = new Vector2(0f, 1f);
        logoR.anchoredPosition = new Vector2(60f, -36f);
        logoR.sizeDelta        = new Vector2(logoW, logoH);

        // Tagline: centro X = borda esquerda do logo + metade da largura
        var sub = Text("Tagline", canvas,
                       "CADA MENTIRA DEIXA UMA MARCA",
                       13f, new Color(MenuAccent.r, MenuAccent.g, MenuAccent.b, 0.70f),
                       16f, TextAlignmentOptions.Center);
        var subR = sub.rectTransform;
        subR.anchorMin        = new Vector2(0f, 1f);
        subR.anchorMax        = new Vector2(0f, 1f);
        subR.pivot            = new Vector2(0.5f, 1f);
        subR.anchoredPosition = new Vector2(60f + logoW * 0.5f, -36f - logoH + 8f);
        subR.sizeDelta        = new Vector2(logoW, 20f);

        // ── Linha vermelha separadora full-width acima dos botões ──
        var divider = Panel("ButtonDivider", canvas, new Color(MenuAccent.r, MenuAccent.g, MenuAccent.b, 0.45f));
        divider.GetComponent<Image>().raycastTarget = false;
        var dvr = Rect(divider);
        dvr.anchorMin        = new Vector2(0f, 0f);
        dvr.anchorMax        = new Vector2(1f, 0f);
        dvr.pivot            = new Vector2(0.5f, 0f);
        dvr.anchoredPosition = new Vector2(0f, 152f);
        dvr.sizeDelta        = new Vector2(0f, 1f);

        // ── Linha horizontal de botões — base centrada ──
        var row = new GameObject("ButtonRow", typeof(RectTransform));
        row.transform.SetParent(canvas, false);
        var rowR = Rect(row);
        rowR.anchorMin        = new Vector2(0.5f, 0f);
        rowR.anchorMax        = new Vector2(0.5f, 0f);
        rowR.pivot            = new Vector2(0.5f, 0f);
        rowR.anchoredPosition = new Vector2(0f, 44f);
        rowR.sizeDelta        = new Vector2(1240f, 76f);

        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing               = 16f;
        hlg.childAlignment        = TextAnchor.MiddleCenter;
        hlg.childControlWidth     = false;
        hlg.childControlHeight    = false;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;

        var btnSize   = new Vector2(396f, 76f);
        var startBtn  = MenuButton("Btn_Play",     row.transform, "Jogar",         btnSize);
        var configBtn = MenuButton("Btn_Settings", row.transform, "Configurações", btnSize);
        var quitBtn   = MenuButton("Btn_Quit",     row.transform, "Sair",          btnSize);

        // Estilo grunge: fundo mais quente e escuro, filete vermelho sangue
        var btnWarm = new Color(0.09f, 0.065f, 0.055f, 1f); // preto-marrom envelhecido
        foreach (var btn in new[] { startBtn, configBtn, quitBtn })
        {
            // Fundo quente
            var bg = btn.GetComponent<Image>();
            if (bg != null) bg.color = btnWarm;

            // Filete vermelho
            var accent = btn.transform.Find(btn.gameObject.name + "_Accent")?.GetComponent<Image>();
            if (accent != null) accent.color = MenuAccent;

            // Texto levemente maior e com mais espaçamento para o estilo impactante
            var label = btn.transform.Find(btn.gameObject.name + "_Label")
                                     ?.GetComponent<TMP_Text>();
            if (label != null)
            {
                label.fontSize         = 22f;
                label.characterSpacing = 16f;
            }
        }

        // ── MainMenuController ──
        var ctrl = canvasGo.AddComponent<MainMenuController>();
        SettingsPanel settingsComp = settingsGo != null
            ? settingsGo.GetComponent<SettingsPanel>() : null;

        var so = new SerializedObject(ctrl);
        so.FindProperty("root").objectReferenceValue = root;
        so.FindProperty("gameScene").stringValue     = "CaseSelectionScene";
        if (settingsComp != null)
            so.FindProperty("settingsPanel").objectReferenceValue = settingsComp;
        so.ApplyModifiedPropertiesWithoutUndo();

        Wire(startBtn, ctrl, "StartGame");
        Wire(quitBtn,  ctrl, "QuitGame");

        if (settingsComp != null)
            Wire(configBtn, ctrl, "OpenSettings");
        else
            configBtn.interactable = false;
    }

    // ─────────────────── EventSystem ───────────────────────────────────────

    private static void MakeEventSystem(Scene scene)
    {
        var existing = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<EventSystem>(true))
            .FirstOrDefault();

        if (existing != null)
        {
            if (existing.GetComponent<InputSystemUIInputModule>() == null)
            {
                var old = existing.GetComponent<BaseInputModule>();
                if (old != null) Object.DestroyImmediate(old);
                existing.gameObject.AddComponent<InputSystemUIInputModule>();
            }
            return;
        }

        var go = new GameObject("EventSystem",
            typeof(EventSystem), typeof(InputSystemUIInputModule));
        SceneManager.MoveGameObjectToScene(go, scene);
        Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");
    }

    // ─────────────────── Build Settings ────────────────────────────────────

    private const string CaseSelectionPath = "Assets/Scenes/CaseSelectionScene.unity";

    private static void EnsureInBuildSettings()
    {
        var scenes = EditorBuildSettings.scenes.ToList();

        if (!scenes.Any(s => s.path == MenuScenePath))
        {
            scenes.Insert(0, new EditorBuildSettingsScene(MenuScenePath, true));
            Debug.Log("[MenuDef] MainMenu adicionada ao Build Settings como cena 0.");
        }

        if (!scenes.Any(s => s.path == CaseSelectionPath))
        {
            scenes.Add(new EditorBuildSettingsScene(CaseSelectionPath, true));
            Debug.Log("[MenuDef] CaseSelectionScene adicionada ao Build Settings.");
        }

        EditorBuildSettings.scenes = scenes.ToArray();
    }

    // ─────────────────── helpers ────────────────────────────────────────────

    private static void Wire(Button btn, Object target, string method)
    {
        if (btn == null || target == null) return;
        for (int i = btn.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
            UnityEditor.Events.UnityEventTools.RemovePersistentListener(btn.onClick, i);
        var call = System.Delegate.CreateDelegate(
            typeof(UnityEngine.Events.UnityAction), target, method)
            as UnityEngine.Events.UnityAction;
        UnityEditor.Events.UnityEventTools.AddPersistentListener(btn.onClick, call);
    }

    /// <summary>Ancora top-left e posiciona pelo offset.</summary>
    private static void AnchorTopLeft(RectTransform r, Vector2 offset, Vector2 size)
    {
        r.anchorMin        = new Vector2(0f, 1f);
        r.anchorMax        = new Vector2(0f, 1f);
        r.pivot            = new Vector2(0f, 1f);
        r.anchoredPosition = offset;
        r.sizeDelta        = size;
    }

    private static Transform FindDeepChild(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        foreach (Transform child in parent)
        {
            var result = FindDeepChild(child, name);
            if (result != null) return result;
        }
        return null;
    }
}
