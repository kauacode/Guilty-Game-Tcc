using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Ferramenta de setup da Fase 2 (UI moderna por atalho).
/// Roda uma vez no Editor: transforma o "Background Panel" full-screen em um
/// cartão flutuante de vidro (glassmorphism), reagrupa os elementos de UI
/// dentro dele e adiciona o InterrogationUIToggle no Canvas.
/// Não salva a cena automaticamente — revise visualmente e salve com Ctrl+S.
/// </summary>
public static class GuiltySceneSetupUI
{
    private const string ShaderName = "Guilty/UIBlur";
    private const string MaterialPath = "Assets/Materials/Mat_UIBlur.mat";

    [MenuItem("Guilty/Fase 2 - Setup UI Moderna")]
    public static void SetupUI()
    {
        GameObject canvasGO = GameObject.Find("Canvas");
        GameObject backgroundPanelGO = GameObject.Find("Background Panel");

        if (canvasGO == null || backgroundPanelGO == null)
        {
            Debug.LogError("[GuiltySetup] Canvas ou Background Panel não encontrados na cena.");
            return;
        }

        RestyleGlassPanel(backgroundPanelGO);
        ReparentIntoPanel(backgroundPanelGO);
        UpdateCanvasScaler(canvasGO);
        AttachToggleController(canvasGO);
        SeparateLoadingCanvasGroup(canvasGO, backgroundPanelGO);

        Debug.Log("[GuiltySetup] Fase 2 concluída. Ajuste posições/tamanhos no Editor se necessário e salve a cena manualmente.");
    }

    private static void RestyleGlassPanel(GameObject panelGO)
    {
        RectTransform rect = panelGO.GetComponent<RectTransform>();
        Undo.RecordObject(rect, "Restyle Glass Panel");
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(760f, 620f);
        rect.anchoredPosition = Vector2.zero;

        Image image = panelGO.GetComponent<Image>();
        Undo.RecordObject(image, "Restyle Glass Panel Image");

        Shader blurShader = Shader.Find(ShaderName);
        if (blurShader != null)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                mat = new Material(blurShader);
                mat.SetColor("_TintColor", new Color(0.04f, 0.04f, 0.07f, 0.55f));
                mat.SetFloat("_BlurSize", 3f);
                System.IO.Directory.CreateDirectory("Assets/Materials");
                AssetDatabase.CreateAsset(mat, MaterialPath);
                Debug.Log("[GuiltySetup] Material de blur criado em " + MaterialPath);
            }

            image.material = mat;
            image.color = Color.white;
        }
        else
        {
            Debug.LogWarning("[GuiltySetup] Shader 'Guilty/UIBlur' não foi encontrado (não compilou?). Usando fallback sólido translúcido.");
            image.material = null;
            image.color = new Color(0.04f, 0.04f, 0.07f, 0.85f);
        }

        VerticalLayoutGroup layout = panelGO.GetComponent<VerticalLayoutGroup>();
        if (layout == null)
        {
            layout = panelGO.AddComponent<VerticalLayoutGroup>();
        }

        layout.padding = new RectOffset(28, 28, 28, 28);
        layout.spacing = 16f;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.UpperCenter;
    }

    private static void ReparentIntoPanel(GameObject panelGO)
    {
        string[] childNames =
        {
            "Response Panel",
            "Detective Text",
            "Status Text",
            "Suspicion Slider",
            "Input Field",
            "Send Button"
        };

        foreach (string name in childNames)
        {
            GameObject child = GameObject.Find(name);
            if (child == null || child == panelGO)
            {
                continue;
            }

            if (child.transform.IsChildOf(panelGO.transform))
            {
                continue;
            }

            Undo.SetTransformParent(child.transform, panelGO.transform, "Reparent into Glass Panel");
            child.transform.SetAsLastSibling();
        }
    }

    private static void UpdateCanvasScaler(GameObject canvasGO)
    {
        CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            return;
        }

        Undo.RecordObject(scaler, "Update Canvas Scaler");
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
    }

    private static void AttachToggleController(GameObject canvasGO)
    {
        InterrogationUIToggle toggle = canvasGO.GetComponent<InterrogationUIToggle>();
        if (toggle == null)
        {
            toggle = canvasGO.AddComponent<InterrogationUIToggle>();
            Undo.RegisterCreatedObjectUndo(toggle, "Add InterrogationUIToggle");
            Debug.Log("[GuiltySetup] InterrogationUIToggle adicionado ao Canvas.");
        }
    }

    /// <summary>
    /// O CanvasGroup do Canvas agora pertence ao InterrogationUIToggle
    /// (controla se o painel inteiro está visível/aberto). O UIController
    /// usava esse MESMO CanvasGroup para escurecer a UI durante o loading
    /// da API — se deixássemos assim, os dois ficariam brigando pelo mesmo
    /// alpha/interactable. Por isso criamos um CanvasGroup próprio no
    /// Background Panel (o cartão de vidro) e realocamos a referência do
    /// UIController para ele via SerializedObject.
    /// </summary>
    private static void SeparateLoadingCanvasGroup(GameObject canvasGO, GameObject panelGO)
    {
        UIController uiController = canvasGO.GetComponent<UIController>();
        if (uiController == null)
        {
            Debug.LogWarning("[GuiltySetup] UIController não encontrado no Canvas — pulando realocação do CanvasGroup de loading.");
            return;
        }

        CanvasGroup innerGroup = panelGO.GetComponent<CanvasGroup>();
        if (innerGroup == null)
        {
            innerGroup = panelGO.AddComponent<CanvasGroup>();
            Undo.RegisterCreatedObjectUndo(innerGroup, "Add Loading CanvasGroup");
        }

        SerializedObject serializedController = new SerializedObject(uiController);
        SerializedProperty canvasGroupProp = serializedController.FindProperty("canvasGroup");
        if (canvasGroupProp != null)
        {
            canvasGroupProp.objectReferenceValue = innerGroup;
            serializedController.ApplyModifiedProperties();
            Debug.Log("[GuiltySetup] UIController.canvasGroup realocado para o CanvasGroup do Background Panel (evita conflito com o toggle de abrir/fechar).");
        }
        else
        {
            Debug.LogWarning("[GuiltySetup] Campo 'canvasGroup' não encontrado via SerializedObject — verifique manualmente no Inspector do UIController.");
        }
    }
}
