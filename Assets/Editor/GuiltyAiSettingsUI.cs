using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using static GuiltyNoirUI;

/// <summary>
/// Acrescenta a página "Detetive (IA)" à tela de Configurações.
///
/// Mexe só no PREFAB (PF_SettingsPanel): o menu principal usa uma instância
/// dele, então a página chega ao menu sem reconstruir a cena — e sem perder os
/// ajustes que o GuiltyMainMenuDefinitive fez lá.
///
/// O que muda no prefab:
///   Content/Page_Geral  ← tudo que já existia (áudio, vídeo, Voltar), mais o
///                         botão "Detetive (IA)" ao lado do Voltar
///   Content/Page_IA     ← a página nova, com o AiSettingsPage
/// Título e régua ficam fora das páginas: são os mesmos nas duas.
///
/// Idempotente: rodar de novo recria só a página de IA.
/// Rodar por: menu Guilty > UI - Página Detetive (IA).
/// </summary>
public static class GuiltyAiSettingsUI
{
    private const string PrefabPath = "Assets/Prefabs/UI/PF_SettingsPanel.prefab";

    // Medidas da tela existente (GuiltySettingsUI): conteúdo de 880 de largura,
    // seções começando em y=-108, botões de rodapé em y=-514 com 300×54.
    private const float Width = 880f;
    private const float FooterY = -514f;
    private static readonly Vector2 FooterButton = new Vector2(300f, 54f);

    [MenuItem("Guilty/UI - Página Detetive (IA)")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[IA] saia do Play mode antes.");
            return;
        }

        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Build(root);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("[IA] página Detetive (IA) montada em " + PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void Build(GameObject root)
    {
        var sp = root.GetComponent<SettingsPanel>();
        var content = root.transform.Find("Content");
        if (sp == null || content == null)
            throw new System.InvalidOperationException("PF_SettingsPanel sem SettingsPanel/Content.");

        // ── idempotência ──
        var oldAi = content.Find("Page_IA");
        if (oldAi != null) Object.DestroyImmediate(oldAi.gameObject);

        var general = content.Find("Page_Geral") ?? CreatePage("Page_Geral", content).transform;
        var oldBtn = general.Find("Btn_IA");
        if (oldBtn != null) Object.DestroyImmediate(oldBtn.gameObject);

        // ── página geral: o que já existia passa a morar nela ──
        foreach (Transform child in content.Cast<Transform>().ToList())
        {
            if (child == general || child.name == "Title" || child.name == "Rule") continue;
            child.SetParent(general, false);
        }

        var toAi = FooterButtonAt("Btn_IA", general, "Detetive (IA)", Width - FooterButton.x);
        Wire(toAi.onClick, sp, nameof(SettingsPanel.ShowAiPage));

        // ── página Detetive (IA) ──
        var page = CreatePage("Page_IA", content);
        var ai = page.AddComponent<AiSettingsPage>();

        float y = -108f;
        Section(page.transform, "DETETIVE (IA)", y);
        y -= 34f;

        // escolha da IA
        Row("Row_IA", page.transform, "Inteligência", Width, 46f, out var iaSlot);
        Place(iaSlot.parent as RectTransform, y);
        var dropdown = NoirDropdown("Dropdown_IA", iaSlot, new Vector2(440f, 42f));
        dropdown.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;
        y -= 46f + 12f;

        var description = Text("Description", page.transform, "", 14f, TextMuted, 2f);
        PlaceText(description.rectTransform, y, 44f);
        y -= 44f + 16f;

        // Gemini: chave + testar + obter (some quando a IA é local)
        float optionY = y;
        var keyRow = Row("Row_Chave", page.transform, "Chave do Gemini", Width, 46f, out var keySlot);
        Place(keyRow.GetComponent<RectTransform>(), optionY);
        var keyInput = NoirInputField("Input_Chave", keySlot, new Vector2(250f, 42f), "cole sua chave aqui");
        keyInput.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;
        var test = SmallButton("Btn_Testar", keySlot, "Testar", 258f);
        var getKey = SmallButton("Btn_Obter", keySlot, "Obter", 366f);

        // Modelo local: baixar / cancelar / apagar (some quando a IA é o Gemini)
        var modelRow = Row("Row_Modelo", page.transform, "Modelo local", Width, 46f, out var modelSlot);
        Place(modelRow.GetComponent<RectTransform>(), optionY);
        var modelBtn = SmallButton("Btn_Modelo", modelSlot, "Baixar", 0f, 300f);
        y -= 46f + 14f;

        // barra de progresso do download: trilho + preenchimento âmbar
        var bar = Rule("ProgressBar", page.transform, Width, 4f, Divider);
        var barRect = bar.GetComponent<RectTransform>();
        barRect.anchorMin = barRect.anchorMax = new Vector2(0f, 1f);
        barRect.pivot = new Vector2(0f, 1f);
        barRect.anchoredPosition = new Vector2(0f, y);
        var fill = Rule("ProgressBar_Fill", bar.transform, 0f, 0f, Amber);
        var fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(0f, 1f);   // o AiSettingsPage move o x
        fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
        y -= 4f + 16f;

        var status = Text("Status", page.transform, "", 15f, TextMuted, 2f);
        PlaceText(status.rectTransform, y, 64f);

        var back = FooterButtonAt("Btn_Voltar_IA", page.transform, "Voltar", 0f);
        Wire(back.onClick, sp, nameof(SettingsPanel.ShowGeneralPage));

        // ── ligações ──
        var so = new SerializedObject(ai);
        so.FindProperty("providerDropdown").objectReferenceValue = dropdown;
        so.FindProperty("descriptionText").objectReferenceValue  = description;
        so.FindProperty("keyRow").objectReferenceValue           = keyRow;
        so.FindProperty("keyInput").objectReferenceValue         = keyInput;
        so.FindProperty("testKeyButton").objectReferenceValue    = test;
        so.FindProperty("modelRow").objectReferenceValue         = modelRow;
        so.FindProperty("modelButton").objectReferenceValue      = modelBtn;
        so.FindProperty("modelButtonLabel").objectReferenceValue = modelBtn.GetComponentInChildren<TMP_Text>(true);
        so.FindProperty("progressBar").objectReferenceValue      = bar;
        so.FindProperty("progressFill").objectReferenceValue     = fillRect;
        so.FindProperty("statusText").objectReferenceValue       = status;
        so.ApplyModifiedPropertiesWithoutUndo();

        var spSo = new SerializedObject(sp);
        spSo.FindProperty("generalPage").objectReferenceValue = general.gameObject;
        spSo.FindProperty("aiPage").objectReferenceValue      = page;
        spSo.ApplyModifiedPropertiesWithoutUndo();

        // listener dinâmico: o dropdown passa o índice escolhido
        UnityEventTools.AddPersistentListener(dropdown.onValueChanged,
            Delegate<UnityAction<int>>(ai, nameof(AiSettingsPage.OnProviderChanged)));
        UnityEventTools.AddPersistentListener(keyInput.onEndEdit,
            Delegate<UnityAction<string>>(ai, nameof(AiSettingsPage.OnKeyEdited)));
        Wire(test.onClick,     ai, nameof(AiSettingsPage.OnTestKey));
        Wire(getKey.onClick,   ai, nameof(AiSettingsPage.OnGetKey));
        Wire(modelBtn.onClick, ai, nameof(AiSettingsPage.OnModelButton));

        FixDropdownLists(root);

        // nasce fechada: a tela sempre abre na página geral
        page.SetActive(false);
        general.gameObject.SetActive(true);
    }

    /// <summary>
    /// Leva as correções do NoirDropdown aos dropdowns criados antes delas
    /// (ex.: Resolução):
    ///  - a lista era recortada com Mask sobre um Image transparente e abria
    ///    vazia → RectMask2D;
    ///  - o fundo do item era transparente e o hover (que multiplica essa cor)
    ///    nunca aparecia → fundo branco + cores do DropdownItemColors.
    /// </summary>
    private static void FixDropdownLists(GameObject root)
    {
        foreach (var dd in root.GetComponentsInChildren<TMP_Dropdown>(true))
        {
            if (dd.template == null) continue;

            var viewport = dd.template.GetComponent<ScrollRect>()?.viewport;
            var mask = viewport != null ? viewport.GetComponent<Mask>() : null;
            if (mask != null)
            {
                Object.DestroyImmediate(mask);
                if (viewport.GetComponent<RectMask2D>() == null) viewport.gameObject.AddComponent<RectMask2D>();
            }

            var item = dd.template.GetComponentInChildren<Toggle>(true);
            if (item != null && item.targetGraphic != null)
            {
                item.targetGraphic.color = Color.white;
                item.colors = DropdownItemColors(item.colors);
            }
        }
    }

    // ─────────────────────────────── apoio ───────────────────────────────

    private static GameObject CreatePage(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var r = go.GetComponent<RectTransform>();
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
        return go;
    }

    private static void Section(Transform parent, string label, float y)
    {
        var t = Text("Section_" + label, parent, label, 13f, Amber, LabelSpacing);
        PlaceText(t.rectTransform, y, 18f, 400f, 2f);
    }

    private static void Place(RectTransform row, float y)
    {
        row.anchorMin = row.anchorMax = new Vector2(0f, 1f);
        row.pivot = new Vector2(0f, 1f);
        row.anchoredPosition = new Vector2(0f, y);
    }

    private static void PlaceText(RectTransform r, float y, float height, float width = Width, float x = 2f)
    {
        r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
        r.pivot = new Vector2(0f, 1f);
        r.anchoredPosition = new Vector2(x, y);
        r.sizeDelta = new Vector2(width, height);
    }

    private static Button FooterButtonAt(string name, Transform parent, string label, float x)
    {
        var b = MenuButton(name, parent, label, FooterButton);
        var r = b.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
        r.pivot = new Vector2(0f, 1f);
        r.anchoredPosition = new Vector2(x, FooterY);
        // "DETETIVE (IA)" no tamanho padrão do rótulo estoura os 300 px
        var t = b.GetComponentInChildren<TMP_Text>(true);
        t.fontSize = 18f;
        t.characterSpacing = 6f;
        return b;
    }

    /// <summary>Botão de linha: mesma caixa do MenuButton, rótulo menor.</summary>
    private static Button SmallButton(string name, RectTransform slot, string label,
                                      float x, float width = 100f)
    {
        var b = MenuButton(name, slot, label, new Vector2(width, 42f));
        var r = b.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = new Vector2(0f, 0.5f);
        r.pivot = new Vector2(0f, 0.5f);
        r.anchoredPosition = new Vector2(x, 0f);
        var t = b.GetComponentInChildren<TMP_Text>(true);
        t.fontSize = 15f;
        t.characterSpacing = 4f;
        t.rectTransform.offsetMin = new Vector2(14f, 0f);
        t.rectTransform.offsetMax = new Vector2(-8f, 0f);
        return b;
    }

    private static T Delegate<T>(Object target, string method) where T : System.Delegate =>
        (T)System.Delegate.CreateDelegate(typeof(T), target, method);

    private static void Wire(UnityEvent e, Object target, string method)
    {
        for (int i = e.GetPersistentEventCount() - 1; i >= 0; i--)
            UnityEventTools.RemovePersistentListener(e, i);
        UnityEventTools.AddPersistentListener(e, Delegate<UnityAction>(target, method));
    }
}
