using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static GuiltyNoirUI;

/// <summary>
/// Monta a tela de Configurações, salva como prefab e liga no botão do menu.
///
/// A tela é um PREFAB (Assets/Prefabs/UI/PF_SettingsPanel.prefab) justamente
/// para o Pause poder abrir a mesma tela depois — sem duplicar UI nem lógica.
///
/// Rodar por: menu Guilty > UI - Montar Tela de Configurações.
/// </summary>
[InitializeOnLoad]
public static class GuiltySettingsUI
{
    private const string MenuScene  = "Assets/Scenes/MainMenu.unity";
    private const string PrefabDir  = "Assets/Prefabs/UI";
    private const string PrefabPath = PrefabDir + "/PF_SettingsPanel.prefab";

    private static string MarkerPath =>
        Path.Combine(Directory.GetParent(Application.dataPath).FullName, ".guilty_settings_done");

    static GuiltySettingsUI()
    {
        if (File.Exists(MarkerPath)) return;
        EditorApplication.delayCall += () =>
        {
            if (File.Exists(MarkerPath)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MenuScene) == null) return;
            File.WriteAllText(MarkerPath, System.DateTime.Now.ToString("o"));
            try { Run(); }
            catch (System.Exception e) { Debug.LogError("[Settings] auto-setup falhou: " + e); }
        };
    }

    [MenuItem("Guilty/UI - Montar Tela de Configurações")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Settings] saia do Play mode antes.");
            return;
        }

        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != MenuScene)
            scene = EditorSceneManager.OpenScene(MenuScene, OpenSceneMode.Single);

        var canvasGo = scene.GetRootGameObjects()
            .FirstOrDefault(g => g.GetComponent<Canvas>() != null);
        if (canvasGo == null) { Debug.LogError("[Settings] Canvas do menu não encontrado."); return; }

        // idempotente
        var old = canvasGo.transform.Find("Settings Panel");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var panel = Build(canvasGo.transform, out var sp, out var back);

        // ── liga o botão Configurações do menu ──
        var configBtn = canvasGo.GetComponentsInChildren<Button>(true)
            .FirstOrDefault(b => b.name == "Btn_Config");
        if (configBtn != null)
        {
            configBtn.interactable = true;

            // o filete e o rótulo tinham sido pintados de "desabilitado"
            var accent = configBtn.transform.Find("Btn_Config_Accent")?.GetComponent<Image>();
            if (accent != null) accent.color = Amber;
            var label = configBtn.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.color = TextHi;

            Wire(configBtn, sp, "Open");
        }
        else Debug.LogWarning("[Settings] Btn_Config não encontrado no menu.");

        // o aviso "não implementadas" perdeu a razão de existir
        var hint = canvasGo.GetComponentsInChildren<TMP_Text>(true)
            .FirstOrDefault(t => t.name == "ConfigHint");
        if (hint != null) hint.gameObject.SetActive(false);

        Wire(back, sp, "CloseAndSave");

        // ── prefab reaproveitável ──
        if (!AssetDatabase.IsValidFolder(PrefabDir))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            AssetDatabase.CreateFolder("Assets/Prefabs", "UI");
        }
        PrefabUtility.SaveAsPrefabAsset(panel, PrefabPath);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        Debug.Log("[Settings] tela pronta.\n  prefab: " + PrefabPath +
                  "\n  botão Configurações do menu ligado");
    }

    private static GameObject Build(Transform parent, out SettingsPanel sp, out Button back)
    {
        var root = FullScreen("Settings Panel", parent, new Color(Ink.r, Ink.g, Ink.b, 0.97f));
        var group = root.AddComponent<CanvasGroup>();
        sp = root.AddComponent<SettingsPanel>();

        var block = new GameObject("Content", typeof(RectTransform));
        block.transform.SetParent(root.transform, false);
        var br = Rect(block);
        br.anchorMin = br.anchorMax = new Vector2(0.5f, 0.5f);
        br.pivot = new Vector2(0.5f, 0.5f);
        br.sizeDelta = new Vector2(880f, 620f);

        var title = Text("Title", block.transform, "CONFIGURAÇÕES", 42f, TextHi, TitleSpacing);
        var tir = title.rectTransform;
        tir.anchorMin = tir.anchorMax = new Vector2(0f, 1f);
        tir.pivot = new Vector2(0f, 1f);
        tir.anchoredPosition = new Vector2(0f, 0f);
        tir.sizeDelta = new Vector2(880f, 56f);

        var rule = Rule("Rule", block.transform, 110f, 3f, Amber);
        var rr = Rect(rule);
        rr.anchorMin = rr.anchorMax = new Vector2(0f, 1f);
        rr.pivot = new Vector2(0f, 1f);
        rr.anchoredPosition = new Vector2(2f, -62f);

        float y = -108f;
        y = Section(block.transform, "ÁUDIO", y);

        var master = SliderRow(block.transform, "Volume geral", ref y, out var masterVal);
        var music  = SliderRow(block.transform, "Música",       ref y, out var musicVal);
        var sfx    = SliderRow(block.transform, "Efeitos",      ref y, out var sfxVal);

        var notice = Text("AudioNotice", block.transform,
            "O jogo ainda não possui sons. Os controles funcionam, mas não haverá nada audível.",
            13f, new Color(Amber.r, Amber.g, Amber.b, 0.85f), 4f);
        var nr = notice.rectTransform;
        nr.anchorMin = nr.anchorMax = new Vector2(0f, 1f);
        nr.pivot = new Vector2(0f, 1f);
        nr.anchoredPosition = new Vector2(2f, y - 4f);
        nr.sizeDelta = new Vector2(820f, 20f);
        y -= 42f;

        y = Section(block.transform, "VÍDEO", y);

        Row("Row_Res", block.transform, "Resolução", 880f, 46f, out var resSlot);
        PlaceRow(resSlot.parent as RectTransform, ref y, 46f, 14f);
        var dd = NoirDropdown("Dropdown_Res", resSlot, new Vector2(300f, 42f));
        CenterInSlot(dd.GetComponent<RectTransform>(), resSlot);

        Row("Row_Full", block.transform, "Tela cheia", 880f, 46f, out var fullSlot);
        PlaceRow(fullSlot.parent as RectTransform, ref y, 46f, 14f);
        var toggle = SquareToggle("Toggle_Fullscreen", fullSlot);
        CenterInSlot(toggle.GetComponent<RectTransform>(), fullSlot);

        back = MenuButton("Btn_Back", block.transform, "Voltar", new Vector2(300f, 54f));
        var bkr = back.GetComponent<RectTransform>();
        bkr.anchorMin = bkr.anchorMax = new Vector2(0f, 1f);
        bkr.pivot = new Vector2(0f, 1f);
        bkr.anchoredPosition = new Vector2(0f, y - 26f);

        // ── liga os controles ao SettingsPanel ──
        var so = new SerializedObject(sp);
        so.FindProperty("masterSlider").objectReferenceValue = master;
        so.FindProperty("musicSlider").objectReferenceValue  = music;
        so.FindProperty("sfxSlider").objectReferenceValue    = sfx;
        so.FindProperty("masterValue").objectReferenceValue  = masterVal;
        so.FindProperty("musicValue").objectReferenceValue   = musicVal;
        so.FindProperty("sfxValue").objectReferenceValue     = sfxVal;
        so.FindProperty("audioNotice").objectReferenceValue  = notice;
        so.FindProperty("resolutionDropdown").objectReferenceValue = dd;
        so.FindProperty("fullscreenToggle").objectReferenceValue   = toggle;
        so.FindProperty("group").objectReferenceValue = group;
        so.ApplyModifiedPropertiesWithoutUndo();

        WireFloat(master, sp, "OnMasterChanged");
        WireFloat(music,  sp, "OnMusicChanged");
        WireFloat(sfx,    sp, "OnSfxChanged");
        WireInt(dd,       sp, "OnResolutionChanged");
        WireBool(toggle,  sp, "OnFullscreenChanged");

        // nasce fechado: em edit mode o Awake não roda, e sem isto o painel
        // ficaria cobrindo o menu na Scene view
        group.alpha = 0f;
        root.SetActive(false);

        return root;
    }

    // ─────────────────────────────── apoio ───────────────────────────────

    private static float Section(Transform parent, string label, float y)
    {
        var t = Text("Section_" + label, parent, label, 13f, Amber, LabelSpacing);
        var r = t.rectTransform;
        r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
        r.pivot = new Vector2(0f, 1f);
        r.anchoredPosition = new Vector2(2f, y);
        r.sizeDelta = new Vector2(400f, 18f);
        return y - 34f;
    }

    private static Slider SliderRow(Transform parent, string label, ref float y, out TMP_Text value)
    {
        Row("Row_" + label, parent, label, 880f, 40f, out var slot);
        PlaceRow(slot.parent as RectTransform, ref y, 40f, 10f);

        var s = HSlider("Slider_" + label, slot, new Vector2(300f, 24f));
        var sr = s.GetComponent<RectTransform>();
        sr.anchorMin = sr.anchorMax = new Vector2(0f, 0.5f);
        sr.pivot = new Vector2(0f, 0.5f);
        sr.anchoredPosition = new Vector2(0f, 0f);

        value = Text("Value_" + label, slot, "80%", 15f, TextHi, 4f, TextAlignmentOptions.Right);
        value.enableWordWrapping = false;
        var vr = value.rectTransform;
        vr.anchorMin = new Vector2(1f, 0.5f); vr.anchorMax = new Vector2(1f, 0.5f);
        vr.pivot = new Vector2(1f, 0.5f);
        vr.sizeDelta = new Vector2(70f, 24f);
        vr.anchoredPosition = Vector2.zero;
        return s;
    }

    private static void PlaceRow(RectTransform row, ref float y, float h, float gap)
    {
        row.anchorMin = row.anchorMax = new Vector2(0f, 1f);
        row.pivot = new Vector2(0f, 1f);
        row.anchoredPosition = new Vector2(0f, y);
        y -= h + gap;
    }

    private static void CenterInSlot(RectTransform r, RectTransform slot)
    {
        r.anchorMin = r.anchorMax = new Vector2(0f, 0.5f);
        r.pivot = new Vector2(0f, 0.5f);
        r.anchoredPosition = Vector2.zero;
    }

    private static void Wire(Button b, Object target, string method)
    {
        for (int i = b.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
            UnityEditor.Events.UnityEventTools.RemovePersistentListener(b.onClick, i);
        var a = System.Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction), target, method)
                as UnityEngine.Events.UnityAction;
        UnityEditor.Events.UnityEventTools.AddPersistentListener(b.onClick, a);
    }

    private static void WireFloat(Slider s, Object target, string method)
    {
        var a = System.Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction<float>), target, method)
                as UnityEngine.Events.UnityAction<float>;
        UnityEditor.Events.UnityEventTools.AddPersistentListener(s.onValueChanged, a);
    }

    private static void WireInt(TMP_Dropdown d, Object target, string method)
    {
        var a = System.Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction<int>), target, method)
                as UnityEngine.Events.UnityAction<int>;
        UnityEditor.Events.UnityEventTools.AddPersistentListener(d.onValueChanged, a);
    }

    private static void WireBool(Toggle t, Object target, string method)
    {
        var a = System.Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction<bool>), target, method)
                as UnityEngine.Events.UnityAction<bool>;
        UnityEditor.Events.UnityEventTools.AddPersistentListener(t.onValueChanged, a);
    }
}
