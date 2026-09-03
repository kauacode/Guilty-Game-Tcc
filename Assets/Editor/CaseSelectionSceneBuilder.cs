using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Gera / Reconstrói a CaseSelectionScene completa.
/// Menu: Guilty → Rebuild Case Selection Scene
/// </summary>
public static class CaseSelectionSceneBuilder
{
    private const string SCENE_PATH   = "Assets/Scenes/CaseSelectionScene.unity";
    private const string FBX_PATH     = "Assets/Models/InvestigationBoard.fbx";
    private const string TARGET_SCENE = "SampleScene";

    private const float CAM_Z   = -1.1f;
    private const float CAM_FOV = 60f;

    [MenuItem("Guilty/Rebuild Case Selection Scene")]
    public static void RebuildScene()
    {
        if (System.IO.File.Exists(SCENE_PATH))
            AssetDatabase.DeleteAsset(SCENE_PATH);
        BuildScene();
    }

    [MenuItem("Guilty/Build Case Selection Scene")]
    public static void BuildScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "CaseSelectionScene";

        RenderSettings.skybox      = null;
        RenderSettings.ambientMode  = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.06f, 0.04f, 0.06f);

        // ── 1. Câmera ─────────────────────────────────────────────────────────
        var camGO = new GameObject("BoardCamera");
        var cam   = camGO.AddComponent<Camera>();
        cam.fieldOfView   = CAM_FOV;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane  = 20f;
        cam.backgroundColor = Color.black;
        cam.clearFlags = CameraClearFlags.SolidColor;
        camGO.transform.position = new Vector3(0f, 0.02f, CAM_Z);
        camGO.transform.rotation = Quaternion.identity;
        camGO.AddComponent<UniversalAdditionalCameraData>();
        camGO.AddComponent<AudioListener>();

        // ── 2. Geometria de fundo ─────────────────────────────────────────────
        var wallGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
        wallGO.name = "OfficeWall";
        Object.DestroyImmediate(wallGO.GetComponent<MeshCollider>());
        wallGO.transform.position   = new Vector3(0f, 0.12f, 0.25f);
        wallGO.transform.rotation   = Quaternion.Euler(0f, 180f, 0f);
        wallGO.transform.localScale = new Vector3(5.5f, 3.8f, 1f);
        wallGO.GetComponent<Renderer>().sharedMaterial = CreateUrpMat("M_OfficeWall",
            new Color(0.16f, 0.12f, 0.09f), smoothness: 0.05f, metallic: 0f);

        var floorGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
        floorGO.name = "OfficeFloor";
        Object.DestroyImmediate(floorGO.GetComponent<MeshCollider>());
        floorGO.transform.position   = new Vector3(0f, -0.80f, -0.2f);
        floorGO.transform.rotation   = Quaternion.Euler(90f, 0f, 0f);
        floorGO.transform.localScale = new Vector3(5.5f, 3.5f, 1f);
        floorGO.GetComponent<Renderer>().sharedMaterial = CreateUrpMat("M_OfficeFloor",
            new Color(0.10f, 0.08f, 0.06f), smoothness: 0.12f, metallic: 0f);

        // ── 3. InvestigationBoard (FBX) ───────────────────────────────────────
        var boardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FBX_PATH);
        if (boardPrefab == null)
        {
            Debug.LogError($"[SceneBuilder] FBX não encontrado: {FBX_PATH}");
        }
        else
        {
            var boardGO = (GameObject)PrefabUtility.InstantiatePrefab(boardPrefab);
            boardGO.name = "InvestigationBoard";
            boardGO.transform.position   = Vector3.zero;
            boardGO.transform.rotation   = Quaternion.identity;
            boardGO.transform.localScale = Vector3.one;

            ApplyBoardMaterials(boardGO);

            AddCaseItem(boardGO, "Case_01", new CaseInfo
            {
                caseId          = "CASO 01",
                caseTitle       = "O Homem da Gaveta",
                statusBadge     = "INVESTIGAÇÃO ATIVA",
                caseDescription = "Um empresário encontrado morto em seu escritório. Sem sinais de arrombamento. O suspeito principal nega qualquer envolvimento, mas as evidências apontam em outra direção.",
                targetSceneName = TARGET_SCENE,
                isLocked        = false
            });

            AddCaseItem(boardGO, "Case_02", new CaseInfo
            {
                caseId          = "CASO 02",
                caseTitle       = "A Mulher Sem Nome",
                statusBadge     = "AGUARDANDO INTERROGATÓRIO",
                caseDescription = "Vítima encontrada sem identificação no porto. Identidade ainda não confirmada. Testemunhas relatam ter visto uma figura encapuzada na noite do crime.",
                targetSceneName = TARGET_SCENE,
                isLocked        = true
            });

            AddCaseItem(boardGO, "Case_03", new CaseInfo
            {
                caseId          = "CASO 03",
                caseTitle       = "O Incêndio da 5ª Rua",
                statusBadge     = "DOSSIÊ LACRADO",
                caseDescription = "Incêndio criminoso que destruiu três andares de um edifício comercial. Três vítimas fatais. Análise forense indica uso de acelerador.",
                targetSceneName = TARGET_SCENE,
                isLocked        = true
            });
        }

        // ── 4. Iluminação ─────────────────────────────────────────────────────
        CreateSpot("MainLight_Desk",
            pos: new Vector3(0f, 2.0f, -0.5f),
            euler: new Vector3(65f, 0f, 0f),
            color: new Color(1.0f, 0.84f, 0.60f),
            intensity: 4.5f, range: 5f, spotAngle: 65f, innerAngle: 52f, shadows: true);

        CreatePoint("FillLight_Left",
            pos: new Vector3(-2.2f, 0.6f, -0.3f),
            color: new Color(0.55f, 0.68f, 1.0f),
            intensity: 0.9f, range: 4.5f);

        CreateSpot("RimLight_Right",
            pos: new Vector3(1.8f, 1.4f, -0.3f),
            euler: new Vector3(25f, -55f, 0f),
            color: new Color(1.0f, 0.70f, 0.35f),
            intensity: 2.2f, range: 4f, spotAngle: 75f, innerAngle: 55f, shadows: false);

        CreatePoint("FillLight_Front",
            pos: new Vector3(0f, 0.2f, -1.0f),
            color: new Color(0.60f, 0.55f, 0.50f),
            intensity: 0.4f, range: 3.0f);

        // ── 5. Global Volume URP ──────────────────────────────────────────────
        var volGO  = new GameObject("GlobalVolume");
        var volume = volGO.AddComponent<Volume>();
        volume.isGlobal = true;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();

        var vig = profile.Add<Vignette>(true);
        vig.intensity.Override(0.38f);
        vig.smoothness.Override(0.35f);
        vig.color.Override(new Color(0.02f, 0.01f, 0.03f));

        var ca = profile.Add<ColorAdjustments>(true);
        ca.contrast.Override(22f);
        ca.saturation.Override(-20f);
        ca.colorFilter.Override(new Color(1.0f, 0.95f, 0.84f));

        var bl = profile.Add<Bloom>(true);
        bl.threshold.Override(0.95f);
        bl.intensity.Override(0.35f);
        bl.scatter.Override(0.60f);

        var smh = profile.Add<ShadowsMidtonesHighlights>(true);
        smh.shadows.Override(new Vector4(0.88f, 0.84f, 1.04f, 0f));
        smh.highlights.Override(new Vector4(1.04f, 0.96f, 0.86f, 0f));

        var tm = profile.Add<Tonemapping>(true);
        tm.mode.Override(TonemappingMode.ACES);

        AssetDatabase.CreateAsset(profile, "Assets/Settings/CaseSelectionVolumeProfile.asset");
        volume.sharedProfile = profile;

        // ── 6. Canvas de fade ─────────────────────────────────────────────────
        var (_, fadeCG) = CreateFadeCanvas();

        // ── 7. Canvas de instruções ───────────────────────────────────────────
        var instrTMP = CreateInstructionCanvas();

        // ── 8. Painel de Dossiê ───────────────────────────────────────────────
        var dossier = CreateDossierPanel();

        // ── 9. BoardSelectionController ───────────────────────────────────────
        var ctrlGO = new GameObject("BoardSelectionController");
        var ctrl   = ctrlGO.AddComponent<BoardSelectionController>();
        var so     = new SerializedObject(ctrl);
        so.FindProperty("fadeOverlay").objectReferenceValue    = fadeCG;
        so.FindProperty("boardCamera").objectReferenceValue    = cam;
        so.FindProperty("dossierPanel").objectReferenceValue   = dossier;
        so.FindProperty("instructionText").objectReferenceValue = instrTMP;
        so.ApplyModifiedProperties();

        // ── 10. Salvar ────────────────────────────────────────────────────────
        EditorSceneManager.SaveScene(scene, SCENE_PATH);
        AddToBuildSettings(SCENE_PATH);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[SceneBuilder] CaseSelectionScene reconstruída com painel de dossiê.");
        EditorUtility.DisplayDialog("Guilty — Cena criada!",
            "CaseSelectionScene gerada com sucesso!\n\n" +
            "• Painel lateral de dossiê adicionado (esquerda)\n" +
            "• Labels flutuantes por caso (aparecem no foco)\n" +
            "• Máquina de estados: Browse → Dossiê → Cena\n" +
            "• Case_01 → ativo  |  Case_02/03 → bloqueados\n\n" +
            "Preencha CaseInfo no Inspector de cada CaseItem\n" +
            "e adicione a foto do suspeito na slot SuspectPhoto.\n\n" +
            "Abra a cena e pressione Play para testar.", "OK");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Painel de Dossiê
    // ════════════════════════════════════════════════════════════════════════

    private static DossierPanel CreateDossierPanel()
    {
        const float PANEL_W = 560f;

        var canvasGO = new GameObject("DossierCanvas");
        var canvas   = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        // Root do painel
        var rootGO = new GameObject("DossierRoot");
        rootGO.transform.SetParent(canvasGO.transform, false);
        var rootRT         = rootGO.AddComponent<RectTransform>();
        rootRT.anchorMin   = new Vector2(0f, 0f);
        rootRT.anchorMax   = new Vector2(0f, 1f);
        rootRT.pivot       = new Vector2(0f, 0.5f);
        rootRT.sizeDelta   = new Vector2(PANEL_W, 0f);
        rootRT.anchoredPosition = new Vector2(-PANEL_W, 0f); // escondido

        var cg = rootGO.AddComponent<CanvasGroup>();
        cg.alpha = 0f; cg.interactable = false; cg.blocksRaycasts = false;

        // Background escuro estilo pasta criminal
        var bgGO  = new GameObject("PanelBackground");
        bgGO.transform.SetParent(rootGO.transform, false);
        var bgImg = bgGO.AddComponent<Image>();
        bgImg.color = new Color(0.10f, 0.07f, 0.04f, 0.94f);
        FillRect(bgGO.GetComponent<RectTransform>());

        // Borda direita sutil (visual de pasta)
        var borderGO  = new GameObject("BorderRight");
        borderGO.transform.SetParent(rootGO.transform, false);
        var borderImg = borderGO.AddComponent<Image>();
        borderImg.color = new Color(0.42f, 0.30f, 0.12f, 0.80f);
        var borderRT = borderGO.GetComponent<RectTransform>();
        borderRT.anchorMin = new Vector2(1f, 0f);
        borderRT.anchorMax = new Vector2(1f, 1f);
        borderRT.pivot     = new Vector2(1f, 0.5f);
        borderRT.sizeDelta = new Vector2(3f, 0f);
        borderRT.anchoredPosition = Vector2.zero;

        // Subtítulo decorativo
        var subGO  = new GameObject("Deco_Header");
        subGO.transform.SetParent(rootGO.transform, false);
        var subTMP = subGO.AddComponent<TextMeshProUGUI>();
        subTMP.text      = "ARQUIVO CONFIDENCIAL";
        subTMP.fontSize  = 11f;
        subTMP.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
        subTMP.alignment = TextAlignmentOptions.Center;
        subTMP.color     = new Color(0.55f, 0.38f, 0.14f, 0.75f);
        SetAnchored(subGO.GetComponent<RectTransform>(), new Vector2(0f,1f), new Vector2(1f,1f),
            new Vector2(0.5f,1f), new Vector2(0f, -16f), new Vector2(-24f, 22f));

        // Linha separadora
        var divGO  = new GameObject("Divider");
        divGO.transform.SetParent(rootGO.transform, false);
        var divImg = divGO.AddComponent<Image>();
        divImg.color = new Color(0.42f, 0.30f, 0.12f, 0.55f);
        SetAnchored(divGO.GetComponent<RectTransform>(), new Vector2(0f,1f), new Vector2(1f,1f),
            new Vector2(0.5f,1f), new Vector2(0f, -42f), new Vector2(-24f, 1f));

        // Título do caso
        var titleGO  = new GameObject("Header_CaseTitle");
        titleGO.transform.SetParent(rootGO.transform, false);
        var titleTMP = titleGO.AddComponent<TextMeshProUGUI>();
        titleTMP.text      = "CASO 01: TÍTULO DO CASO";
        titleTMP.fontSize  = 20f;
        titleTMP.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
        titleTMP.alignment = TextAlignmentOptions.Left;
        titleTMP.color     = new Color(0.95f, 0.85f, 0.55f);
        SetAnchored(titleGO.GetComponent<RectTransform>(), new Vector2(0f,1f), new Vector2(1f,1f),
            new Vector2(0.5f,1f), new Vector2(0f, -52f), new Vector2(-24f, 56f));

        // Badge de status
        var statusGO  = new GameObject("Text_Status");
        statusGO.transform.SetParent(rootGO.transform, false);
        var statusTMP = statusGO.AddComponent<TextMeshProUGUI>();
        statusTMP.text      = "[ AGUARDANDO INTERROGATÓRIO ]";
        statusTMP.fontSize  = 12f;
        statusTMP.fontStyle = FontStyles.Bold;
        statusTMP.alignment = TextAlignmentOptions.Left;
        statusTMP.color     = new Color(0.80f, 0.18f, 0.10f);
        SetAnchored(statusGO.GetComponent<RectTransform>(), new Vector2(0f,1f), new Vector2(1f,1f),
            new Vector2(0.5f,1f), new Vector2(0f, -114f), new Vector2(-24f, 26f));

        // Imagem do suspeito (placeholder)
        var imgGO  = new GameObject("Image_SuspectPlaceholder");
        imgGO.transform.SetParent(rootGO.transform, false);
        var suspectImg   = imgGO.AddComponent<Image>();
        suspectImg.color = new Color(0.14f, 0.10f, 0.07f);
        SetAnchored(imgGO.GetComponent<RectTransform>(), new Vector2(0f,1f), new Vector2(1f,1f),
            new Vector2(0.5f,1f), new Vector2(0f, -148f), new Vector2(-24f, 210f));

        // Descrição do caso
        var descGO  = new GameObject("Text_CaseDescription");
        descGO.transform.SetParent(rootGO.transform, false);
        var descTMP = descGO.AddComponent<TextMeshProUGUI>();
        descTMP.text           = "Descrição do caso...";
        descTMP.fontSize       = 14f;
        descTMP.alignment      = TextAlignmentOptions.TopLeft;
        descTMP.color          = new Color(0.82f, 0.76f, 0.64f);
        descTMP.enableWordWrapping = true;
        var descRT = descGO.GetComponent<RectTransform>();
        descRT.anchorMin = new Vector2(0f, 0f);
        descRT.anchorMax = new Vector2(1f, 1f);
        descRT.offsetMin = new Vector2(12f, 148f);
        descRT.offsetMax = new Vector2(-12f, -370f);

        // Segunda linha separadora acima dos botões
        var div2GO  = new GameObject("Divider2");
        div2GO.transform.SetParent(rootGO.transform, false);
        var div2Img = div2GO.AddComponent<Image>();
        div2Img.color = new Color(0.42f, 0.30f, 0.12f, 0.45f);
        var div2RT = div2GO.GetComponent<RectTransform>();
        div2RT.anchorMin = new Vector2(0f, 0f);
        div2RT.anchorMax = new Vector2(1f, 0f);
        div2RT.pivot     = new Vector2(0.5f, 0f);
        div2RT.anchoredPosition = new Vector2(0f, 147f);
        div2RT.sizeDelta = new Vector2(-24f, 1f);

        // Botão INICIAR INTERROGATÓRIO
        var confirmGO  = CreateButtonElement(rootGO.transform, "Button_Confirm",
            "INICIAR INTERROGATÓRIO",
            new Color(0.50f, 0.08f, 0.04f),
            anchoredPos: new Vector2(0f, 90f),
            height: 52f);

        // Botão VOLTAR
        var backGO = CreateButtonElement(rootGO.transform, "Button_Back",
            "← VOLTAR",
            new Color(0.22f, 0.18f, 0.14f),
            anchoredPos: new Vector2(0f, 26f),
            height: 38f);

        // Monta DossierPanel
        var panel = rootGO.AddComponent<DossierPanel>();
        var pso   = new SerializedObject(panel);
        pso.FindProperty("panelRect").objectReferenceValue       = rootRT;
        pso.FindProperty("panelGroup").objectReferenceValue      = cg;
        pso.FindProperty("panelWidth").floatValue                = PANEL_W;
        pso.FindProperty("titleText").objectReferenceValue       = titleTMP;
        pso.FindProperty("statusText").objectReferenceValue      = statusTMP;
        pso.FindProperty("suspectImage").objectReferenceValue    = suspectImg;
        pso.FindProperty("descriptionText").objectReferenceValue = descTMP;
        pso.FindProperty("confirmButton").objectReferenceValue   = confirmGO.GetComponent<Button>();
        pso.FindProperty("backButton").objectReferenceValue      = backGO.GetComponent<Button>();
        pso.ApplyModifiedProperties();

        return panel;
    }

    private static GameObject CreateButtonElement(Transform parent, string name, string label,
        Color bgColor, Vector2 anchoredPos, float height)
    {
        var go  = new GameObject(name);
        go.transform.SetParent(parent, false);

        var img = go.AddComponent<Image>();
        img.color = bgColor;

        var btn    = go.AddComponent<Button>();
        var colors = btn.colors;
        colors.highlightedColor = bgColor * 1.4f;
        colors.pressedColor     = bgColor * 0.6f;
        colors.normalColor      = Color.white; // multiplicado sobre bgColor
        btn.colors = colors;

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0f, 0f);
        rt.anchorMax        = new Vector2(1f, 0f);
        rt.pivot            = new Vector2(0.5f, 0f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta        = new Vector2(-24f, height);

        var textGO  = new GameObject("Label");
        textGO.transform.SetParent(go.transform, false);
        var tmp       = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text      = label;
        tmp.fontSize  = 14f;
        tmp.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color     = new Color(0.95f, 0.90f, 0.80f);
        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = textRT.offsetMax = Vector2.zero;

        return go;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Materiais URP
    // ════════════════════════════════════════════════════════════════════════

    private static Material CreateUrpMat(string name, Color baseColor,
        float smoothness = 0.5f, float metallic = 0f, Color? emission = null)
    {
        string assetPath = $"Assets/Materials/{name}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
        if (existing != null) return existing;

        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.name = name;
        mat.SetColor("_BaseColor", baseColor);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic", metallic);
        if (emission.HasValue)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emission.Value);
        }
        System.IO.Directory.CreateDirectory("Assets/Materials");
        AssetDatabase.CreateAsset(mat, assetPath);
        return mat;
    }

    private static void ApplyBoardMaterials(GameObject boardRoot)
    {
        var matCork      = CreateUrpMat("M_CorkBoard",      new Color(0.46f, 0.31f, 0.16f), smoothness: 0.03f);
        var matWood      = CreateUrpMat("M_WoodFrame",      new Color(0.13f, 0.07f, 0.03f), smoothness: 0.28f);
        var matPaper     = CreateUrpMat("M_Paper",          new Color(0.90f, 0.86f, 0.74f), smoothness: 0.01f);
        var matJornal    = CreateUrpMat("M_PaperJornal",    new Color(0.82f, 0.74f, 0.48f), smoothness: 0.01f);
        var matPolBord   = CreateUrpMat("M_PolaroidBorder", new Color(0.96f, 0.94f, 0.90f), smoothness: 0.07f);
        var matPolPhoto  = CreateUrpMat("M_PolaroidPhoto",  new Color(0.22f, 0.20f, 0.18f), smoothness: 0.15f);
        var matMap       = CreateUrpMat("M_CityMap",        new Color(0.82f, 0.78f, 0.62f), smoothness: 0.05f);
        var matMapLine   = CreateUrpMat("M_MapLine",        new Color(0.28f, 0.32f, 0.45f), smoothness: 0.10f);
        var matPostYel   = CreateUrpMat("M_PostItYellow",   new Color(0.95f, 0.90f, 0.20f), smoothness: 0.05f);
        var matPostRed   = CreateUrpMat("M_PostItRed",      new Color(0.90f, 0.20f, 0.20f), smoothness: 0.05f);
        var matPostBlu   = CreateUrpMat("M_PostItBlue",     new Color(0.30f, 0.55f, 0.90f), smoothness: 0.05f);
        var matPostGreen = CreateUrpMat("M_PostItGreen",    new Color(0.55f, 0.75f, 0.30f), smoothness: 0.05f);
        var matAnnot     = CreateUrpMat("M_Annotation",     new Color(0.12f, 0.08f, 0.04f), smoothness: 0.10f);
        var matPin       = CreateUrpMat("M_Pin",            new Color(0.80f, 0.04f, 0.04f), smoothness: 0.72f, metallic: 0.15f,
                                        emission: new Color(0.30f, 0.01f, 0.01f));
        var matTube      = CreateUrpMat("M_RedString",      new Color(0.70f, 0.02f, 0.02f), smoothness: 0.10f,
                                        emission: new Color(0.55f, 0.01f, 0.01f));
        var matWall      = CreateUrpMat("M_Wall",           new Color(0.17f, 0.12f, 0.08f), smoothness: 0.03f);
        var matTape      = CreateUrpMat("M_Tape",           new Color(0.94f, 0.92f, 0.76f), smoothness: 0.10f);
        var matInk       = CreateUrpMat("M_Ink",            new Color(0.08f, 0.06f, 0.04f), smoothness: 0.05f);
        var matStampRed  = CreateUrpMat("M_StampRed",       new Color(0.55f, 0.05f, 0.05f), smoothness: 0.15f);
        var matStampDark = CreateUrpMat("M_StampDark",      new Color(0.10f, 0.08f, 0.06f), smoothness: 0.05f);
        var matBadgeBord = CreateUrpMat("M_BadgeBorder",    new Color(0.82f, 0.72f, 0.50f), smoothness: 0.35f, metallic: 0.2f);
        var matPaperHdr  = CreateUrpMat("M_PaperHeader",    new Color(0.62f, 0.54f, 0.38f), smoothness: 0.01f);
        var matCasePhoto = CreateUrpMat("M_CasePhoto",      new Color(0.12f, 0.10f, 0.09f), smoothness: 0.30f);
        var matPinShaft  = CreateUrpMat("M_PinShaft",       new Color(0.75f, 0.75f, 0.78f), smoothness: 0.65f, metallic: 0.85f);

        foreach (var r in boardRoot.GetComponentsInChildren<Renderer>())
        {
            string n = r.gameObject.name;
            if      (n == "IB_Cork")                               ApplyMat(r, matCork);
            else if (n.Contains("Frame"))                          ApplyMat(r, matWood);
            else if (n.Contains("Baseboard") || n.Contains("Cornija")) ApplyMat(r, matWood);
            else if (n == "Case_01")                               ApplyMat(r, matPaper);
            else if (n == "Case_02")                               ApplyMat(r, matJornal);
            else if (n == "Case_03")                               ApplyMat(r, matPaper);
            else if (n.Contains("Header"))                         ApplyMat(r, matPaperHdr);
            else if (n.Contains("Photo") && n.Contains("Bord"))   ApplyMat(r, matPolBord);
            else if (n.Contains("Photo"))                          ApplyMat(r, matCasePhoto);
            else if (n.Contains("Tape"))                           ApplyMat(r, matTape);
            else if (n.Contains("TxtLine"))                        ApplyMat(r, matInk);
            else if (n.Contains("Stamp") && n.Contains("Inner"))   ApplyMat(r, matStampDark);
            else if (n.Contains("Stamp"))                          ApplyMat(r, matStampRed);
            else if (n.Contains("Badge_Border"))                   ApplyMat(r, matBadgeBord);
            else if (n.Contains("Badge"))                          ApplyMat(r, matStampDark);
            else if (n.Contains("AnnExtra"))                       ApplyMat(r, matAnnot);
            else if (n.Contains("Polar") && n.Contains("Border")) ApplyMat(r, matPolBord);
            else if (n.Contains("Polar") && n.Contains("Photo"))  ApplyMat(r, matPolPhoto);
            else if (n == "IB_CityMap")                           ApplyMat(r, matMap);
            else if (n.Contains("MapH") || n.Contains("MapV"))    ApplyMat(r, matMapLine);
            else if (n.Contains("PostIt_Yellow"))                  ApplyMat(r, matPostYel);
            else if (n.Contains("PostIt_Red"))                     ApplyMat(r, matPostRed);
            else if (n.Contains("PostIt_Blue"))                    ApplyMat(r, matPostBlu);
            else if (n.Contains("PostIt_Green"))                   ApplyMat(r, matPostGreen);
            else if (n.Contains("Ann_"))                           ApplyMat(r, matAnnot);
            else if (n.Contains("_Head"))                          ApplyMat(r, matPin);
            else if (n.Contains("_Shaft"))                         ApplyMat(r, matPinShaft);
            else if (n.Contains("Tube") || n.Contains("String"))  ApplyMat(r, matTube);
            else if (n.Contains("Wall"))                           ApplyMat(r, matWall);
        }
    }

    private static void ApplyMat(Renderer r, Material mat)
    {
        var mats = r.sharedMaterials;
        for (int i = 0; i < mats.Length; i++) mats[i] = mat;
        r.sharedMaterials = mats;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Luzes
    // ════════════════════════════════════════════════════════════════════════

    private static Light CreateSpot(string name, Vector3 pos, Vector3 euler,
        Color color, float intensity, float range, float spotAngle, float innerAngle, bool shadows)
    {
        var go = new GameObject(name);
        var l  = go.AddComponent<Light>();
        l.type           = LightType.Spot;
        l.color          = color;
        l.intensity      = intensity;
        l.range          = range;
        l.spotAngle      = spotAngle;
        l.innerSpotAngle = innerAngle;
        l.shadows        = shadows ? LightShadows.Soft : LightShadows.None;
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(euler);
        go.AddComponent<UniversalAdditionalLightData>();
        return l;
    }

    private static Light CreatePoint(string name, Vector3 pos, Color color, float intensity, float range)
    {
        var go = new GameObject(name);
        var l  = go.AddComponent<Light>();
        l.type      = LightType.Point;
        l.color     = color;
        l.intensity = intensity;
        l.range     = range;
        l.shadows   = LightShadows.None;
        go.transform.position = pos;
        go.AddComponent<UniversalAdditionalLightData>();
        return l;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Canvas de instrução (retorna o TMP para wiring)
    // ════════════════════════════════════════════════════════════════════════

    private static TextMeshProUGUI CreateInstructionCanvas()
    {
        var canvasGO = new GameObject("InstructionCanvas");
        var canvas   = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        var barGO = new GameObject("InstructionBar");
        barGO.transform.SetParent(canvasGO.transform, false);
        var barRT         = barGO.AddComponent<RectTransform>();
        barRT.anchorMin   = new Vector2(0f, 0f);
        barRT.anchorMax   = new Vector2(1f, 0f);
        barRT.pivot       = new Vector2(0.5f, 0f);
        barRT.anchoredPosition = new Vector2(0f, 20f);
        barRT.sizeDelta   = new Vector2(0f, 60f);

        var textGO = new GameObject("InstructionText");
        textGO.transform.SetParent(barGO.transform, false);
        var tmp       = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text      = "[ ← / → ]  ou  [ MOUSE ]  Selecionar Caso     |     [ ENTER ]  ou  [ CLIQUE ]  Abrir Dossiê";
        tmp.fontSize  = 22f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color     = new Color(1f, 1f, 1f, 0.72f);

        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = textRT.offsetMax = Vector2.zero;

        return tmp;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Canvas de fade
    // ════════════════════════════════════════════════════════════════════════

    private static (GameObject, CanvasGroup) CreateFadeCanvas()
    {
        var canvasGO = new GameObject("FadeCanvas");
        var canvas   = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 99;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        var panelGO = new GameObject("FadePanel");
        panelGO.transform.SetParent(canvasGO.transform, false);
        var img = panelGO.AddComponent<Image>();
        img.color = Color.black;
        var rt = panelGO.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        var cg = panelGO.AddComponent<CanvasGroup>();
        cg.alpha = 1f;
        return (canvasGO, cg);
    }

    // ════════════════════════════════════════════════════════════════════════
    // CaseItem / colisores
    // ════════════════════════════════════════════════════════════════════════

    private static void AddCaseItem(GameObject boardRoot, string meshName, CaseInfo info)
    {
        var tf = FindDeep(boardRoot.transform, meshName);
        if (tf == null)
        {
            Debug.LogWarning($"[SceneBuilder] '{meshName}' não encontrado no FBX.");
            return;
        }

        var go = tf.gameObject;
        if (go.GetComponent<BoxCollider>() == null)
            go.AddComponent<BoxCollider>();

        var item = go.GetComponent<CaseItem>() ?? go.AddComponent<CaseItem>();
        var so   = new SerializedObject(item);

        // Popula CaseInfo aninhada
        so.FindProperty("caseData.caseId").stringValue          = info.caseId;
        so.FindProperty("caseData.caseTitle").stringValue        = info.caseTitle;
        so.FindProperty("caseData.statusBadge").stringValue      = info.statusBadge;
        so.FindProperty("caseData.caseDescription").stringValue  = info.caseDescription;
        so.FindProperty("caseData.targetSceneName").stringValue  = info.targetSceneName;
        so.FindProperty("caseData.isLocked").boolValue           = info.isLocked;
        // suspectPhoto fica null aqui — atribua manualmente no Inspector

        so.ApplyModifiedProperties();
    }

    private static Transform FindDeep(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            var r = FindDeep(child, name);
            if (r != null) return r;
        }
        return null;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Helpers de RectTransform
    // ════════════════════════════════════════════════════════════════════════

    private static void FillRect(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private static void SetAnchored(RectTransform rt,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 anchoredPos, Vector2 sizeDelta)
    {
        rt.anchorMin        = anchorMin;
        rt.anchorMax        = anchorMax;
        rt.pivot            = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta        = sizeDelta;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Build Settings
    // ════════════════════════════════════════════════════════════════════════

    private static void AddToBuildSettings(string path)
    {
        var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(
            EditorBuildSettings.scenes);
        foreach (var s in list)
            if (s.path == path) return;
        list.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = list.ToArray();
    }
}
