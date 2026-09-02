using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Vocabulário visual compartilhado das telas do jogo (menu, pause, fim de jogo).
///
/// Existe para que as quatro telas passem pelo MESMO código de estilo — é isso
/// que garante consistência, não copiar valores de cor de um lado para o outro.
///
/// A paleta sai do que a HUD já usava: o painel de interrogatório é
/// Color(0.04, 0.04, 0.07, 0.85), e o âmbar é o mesmo da luminária que o filtro
/// noir preserva nos realces. Nada de canto arredondado (sprite = null deixa o
/// retângulo reto), nada de gradiente, nada de ícone.
/// </summary>
public static class GuiltyNoirUI
{
    // ── paleta ──
    public static readonly Color Ink        = new Color(0.035f, 0.038f, 0.055f, 1f);   // fundo cheio
    public static readonly Color Scrim      = new Color(0.035f, 0.038f, 0.055f, 0.88f);// overlay sobre o jogo
    public static readonly Color Surface    = new Color(0.06f, 0.065f, 0.085f, 1f);    // caixa de botão
    public static readonly Color SurfaceHi  = new Color(0.10f, 0.10f, 0.13f, 1f);      // hover
    public static readonly Color Amber      = new Color(0.78f, 0.53f, 0.16f, 1f);      // destaque único
    public static readonly Color Danger     = new Color(0.60f, 0.19f, 0.15f, 1f);
    public static readonly Color TextHi     = new Color(0.91f, 0.90f, 0.88f, 1f);
    // cinza levemente QUENTE. Um cinza frio (0.52,0.53,0.58) puxava para azul
    // contra o fundo quase-preto e brigava com o âmbar, que é o único destaque.
    public static readonly Color TextMuted  = new Color(0.62f, 0.61f, 0.58f, 1f);
    public static readonly Color Divider    = new Color(0.16f, 0.17f, 0.21f, 1f);

    // ── tipografia ──
    public const float TitleSize   = 62f;
    public const float SubtitleSize= 17f;
    public const float BodySize    = 19f;
    public const float ButtonSize  = 20f;
    public const float TitleSpacing   = 22f;   // letter-spacing largo = tom formal
    public const float ButtonSpacing  = 10f;
    public const float LabelSpacing   = 14f;

    public static RectTransform Rect(GameObject go) => go.GetComponent<RectTransform>();

    public static GameObject Panel(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        img.sprite = null;          // retângulo reto, sem canto arredondado
        img.raycastTarget = true;
        return go;
    }

    public static GameObject FullScreen(string name, Transform parent, Color color)
    {
        var go = Panel(name, parent, color);
        var r = Rect(go);
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
        return go;
    }

    public static TMP_Text Text(string name, Transform parent, string content,
                                float size, Color color, float spacing,
                                TextAlignmentOptions align = TextAlignmentOptions.Left)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = content;
        t.fontSize = size;
        t.color = color;
        t.characterSpacing = spacing;
        t.alignment = align;
        t.raycastTarget = false;
        t.enableWordWrapping = true;
        return t;
    }

    /// <summary>
    /// Barra âmbar fina. Usada como régua sob o título e como marca do item
    /// ativo — é o único elemento colorido das telas, de propósito.
    /// </summary>
    public static GameObject Rule(string name, Transform parent, float width, float height, Color color)
    {
        var go = Panel(name, parent, color);
        go.GetComponent<Image>().raycastTarget = false;
        var r = Rect(go);
        r.sizeDelta = new Vector2(width, height);
        return go;
    }

    /// <summary>
    /// Botão institucional: caixa reta escura, filete âmbar à esquerda, rótulo
    /// em caixa alta com letter-spacing. A transição é de cor (Unity cuida do
    /// hover/press), sem escala nem bounce.
    /// </summary>
    public static Button MenuButton(string name, Transform parent, string label,
                                    Vector2 size, bool enabled = true)
    {
        var go = Panel(name, parent, Surface);
        var r = Rect(go);
        r.sizeDelta = size;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = go.GetComponent<Image>();
        btn.transition = Selectable.Transition.ColorTint;
        var cb = btn.colors;
        cb.normalColor      = Color.white;
        cb.highlightedColor = new Color(1.6f, 1.6f, 1.7f, 1f);   // clareia a Surface
        cb.pressedColor     = new Color(0.8f, 0.8f, 0.85f, 1f);
        cb.disabledColor    = new Color(0.6f, 0.6f, 0.6f, 0.5f);
        cb.fadeDuration     = 0.12f;
        btn.colors = cb;
        btn.interactable = enabled;

        // filete âmbar colado na borda esquerda
        var bar = Panel(name + "_Accent", go.transform, enabled ? Amber : Divider);
        bar.GetComponent<Image>().raycastTarget = false;
        var br = Rect(bar);
        br.anchorMin = new Vector2(0f, 0f); br.anchorMax = new Vector2(0f, 1f);
        br.pivot = new Vector2(0f, 0.5f);
        br.sizeDelta = new Vector2(3f, 0f);
        br.anchoredPosition = Vector2.zero;

        var t = Text(name + "_Label", go.transform, label.ToUpperInvariant(),
                     ButtonSize, enabled ? TextHi : TextMuted, ButtonSpacing,
                     TextAlignmentOptions.Left);
        // rótulo de botão nunca quebra linha: com letter-spacing largo é fácil
        // um texto médio virar duas linhas e estourar a altura da caixa
        t.enableWordWrapping = false;
        t.overflowMode = TextOverflowModes.Ellipsis;
        var tr = t.rectTransform;
        tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
        tr.offsetMin = new Vector2(26f, 0f); tr.offsetMax = new Vector2(-18f, 0f);

        return btn;
    }

    /// <summary>
    /// Linha "RÓTULO ......... [controle]". Todas as opções da tela de
    /// Configurações usam esta mesma linha, então rótulo e controle ficam
    /// alinhados sem ninguém posicionar nada à mão.
    /// </summary>
    public static GameObject Row(string name, Transform parent, string label,
                                 float width, float height, out RectTransform slot)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Rect(go).sizeDelta = new Vector2(width, height);

        var t = Text(name + "_Label", go.transform, label.ToUpperInvariant(),
                     15f, TextMuted, LabelSpacing, TextAlignmentOptions.Left);
        t.enableWordWrapping = false;
        var lr = t.rectTransform;
        lr.anchorMin = new Vector2(0f, 0f); lr.anchorMax = new Vector2(0f, 1f);
        lr.pivot = new Vector2(0f, 0.5f);
        lr.sizeDelta = new Vector2(width * 0.42f, 0f);
        lr.anchoredPosition = Vector2.zero;

        var slotGo = new GameObject(name + "_Slot", typeof(RectTransform));
        slotGo.transform.SetParent(go.transform, false);
        slot = Rect(slotGo);
        slot.anchorMin = new Vector2(1f, 0f); slot.anchorMax = new Vector2(1f, 1f);
        slot.pivot = new Vector2(1f, 0.5f);
        slot.sizeDelta = new Vector2(width * 0.54f, 0f);
        slot.anchoredPosition = Vector2.zero;

        return go;
    }

    /// <summary>
    /// Slider reto: trilho de 2px, preenchimento âmbar, alça retangular.
    /// Nada de sprite arredondado do UI padrão.
    /// </summary>
    public static Slider HSlider(string name, Transform parent, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var r = Rect(go);
        r.anchorMin = new Vector2(0f, 0.5f); r.anchorMax = new Vector2(0f, 0.5f);
        r.pivot = new Vector2(0f, 0.5f);
        r.sizeDelta = size;

        var track = Panel(name + "_Track", go.transform, Divider);
        track.GetComponent<Image>().raycastTarget = false;
        var tr = Rect(track);
        tr.anchorMin = new Vector2(0f, 0.5f); tr.anchorMax = new Vector2(1f, 0.5f);
        tr.pivot = new Vector2(0.5f, 0.5f);
        tr.offsetMin = new Vector2(0f, -1f); tr.offsetMax = new Vector2(0f, 1f);

        var fillArea = new GameObject("FillArea", typeof(RectTransform));
        fillArea.transform.SetParent(go.transform, false);
        var far = Rect(fillArea);
        far.anchorMin = new Vector2(0f, 0.5f); far.anchorMax = new Vector2(1f, 0.5f);
        far.offsetMin = new Vector2(0f, -1f); far.offsetMax = new Vector2(0f, 1f);

        var fill = Panel("Fill", fillArea.transform, Amber);
        fill.GetComponent<Image>().raycastTarget = false;
        var fr = Rect(fill);
        fr.anchorMin = Vector2.zero; fr.anchorMax = new Vector2(0f, 1f);
        fr.sizeDelta = new Vector2(0f, 0f);

        var handleArea = new GameObject("HandleArea", typeof(RectTransform));
        handleArea.transform.SetParent(go.transform, false);
        var har = Rect(handleArea);
        har.anchorMin = Vector2.zero; har.anchorMax = Vector2.one;
        har.offsetMin = Vector2.zero; har.offsetMax = Vector2.zero;

        var handle = Panel("Handle", handleArea.transform, TextHi);
        var hr = Rect(handle);
        hr.sizeDelta = new Vector2(6f, 20f);   // retângulo, não círculo

        var s = go.AddComponent<Slider>();
        s.fillRect = fr;
        s.handleRect = hr;
        s.targetGraphic = handle.GetComponent<Image>();
        s.direction = Slider.Direction.LeftToRight;
        s.minValue = 0f; s.maxValue = 1f; s.wholeNumbers = false;

        var cb = s.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1f, 0.86f, 0.55f, 1f);
        cb.pressedColor = Amber;
        cb.fadeDuration = 0.1f;
        s.colors = cb;

        return s;
    }

    /// <summary>Toggle quadrado: caixa vazada, marca âmbar cheia quando ligado.</summary>
    public static Toggle SquareToggle(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var r = Rect(go);
        r.anchorMin = new Vector2(0f, 0.5f); r.anchorMax = new Vector2(0f, 0.5f);
        r.pivot = new Vector2(0f, 0.5f);
        r.sizeDelta = new Vector2(24f, 24f);

        var box = Panel(name + "_Box", go.transform, Surface);
        var bxr = Rect(box);
        bxr.anchorMin = Vector2.zero; bxr.anchorMax = Vector2.one;
        bxr.offsetMin = Vector2.zero; bxr.offsetMax = Vector2.zero;

        var border = Panel(name + "_Border", go.transform, Divider);
        border.GetComponent<Image>().raycastTarget = false;
        var bor = Rect(border);
        bor.anchorMin = Vector2.zero; bor.anchorMax = Vector2.one;
        bor.offsetMin = new Vector2(-1f, -1f); bor.offsetMax = new Vector2(1f, 1f);
        border.transform.SetSiblingIndex(0);

        var check = Panel(name + "_Check", go.transform, Amber);
        check.GetComponent<Image>().raycastTarget = false;
        var cr = Rect(check);
        cr.anchorMin = Vector2.zero; cr.anchorMax = Vector2.one;
        cr.offsetMin = new Vector2(5f, 5f); cr.offsetMax = new Vector2(-5f, -5f);

        var t = go.AddComponent<Toggle>();
        t.targetGraphic = box.GetComponent<Image>();
        t.graphic = check.GetComponent<Image>();
        t.isOn = true;
        return t;
    }

    /// <summary>
    /// Dropdown institucional. O TMP_Dropdown exige um "template" montado à mão
    /// (viewport + content + item), senão ele cai no visual padrão do Unity —
    /// que é exatamente o que a direção de arte pede para evitar.
    /// </summary>
    public static TMP_Dropdown NoirDropdown(string name, Transform parent, Vector2 size)
    {
        var go = Panel(name, parent, Surface);
        var r = Rect(go);
        r.anchorMin = new Vector2(0f, 0.5f); r.anchorMax = new Vector2(0f, 0.5f);
        r.pivot = new Vector2(0f, 0.5f);
        r.sizeDelta = size;

        var accent = Panel(name + "_Accent", go.transform, Amber);
        accent.GetComponent<Image>().raycastTarget = false;
        var ar = Rect(accent);
        ar.anchorMin = new Vector2(0f, 0f); ar.anchorMax = new Vector2(0f, 1f);
        ar.pivot = new Vector2(0f, 0.5f);
        ar.sizeDelta = new Vector2(2f, 0f);

        var caption = Text(name + "_Caption", go.transform, "", 16f, TextHi, 6f,
                           TextAlignmentOptions.Left);
        caption.enableWordWrapping = false;
        var capr = caption.rectTransform;
        capr.anchorMin = Vector2.zero; capr.anchorMax = Vector2.one;
        capr.offsetMin = new Vector2(16f, 0f); capr.offsetMax = new Vector2(-30f, 0f);

        // seta: um traço simples, sem ícone
        var arrow = Text(name + "_Arrow", go.transform, "v", 13f, Amber, 0f,
                         TextAlignmentOptions.Center);
        var arr = arrow.rectTransform;
        arr.anchorMin = new Vector2(1f, 0.5f); arr.anchorMax = new Vector2(1f, 0.5f);
        arr.pivot = new Vector2(1f, 0.5f);
        arr.sizeDelta = new Vector2(26f, 26f);
        arr.anchoredPosition = new Vector2(-6f, 0f);

        // ── template da lista ──
        var template = Panel(name + "_Template", go.transform, new Color(0.045f, 0.05f, 0.07f, 1f));
        var tr = Rect(template);
        tr.anchorMin = new Vector2(0f, 0f); tr.anchorMax = new Vector2(1f, 0f);
        tr.pivot = new Vector2(0.5f, 1f);
        tr.anchoredPosition = new Vector2(0f, -2f);
        tr.sizeDelta = new Vector2(0f, 190f);

        var scroll = template.AddComponent<ScrollRect>();

        var viewport = Panel(name + "_Viewport", template.transform, new Color(0, 0, 0, 0));
        var vr = Rect(viewport);
        vr.anchorMin = Vector2.zero; vr.anchorMax = Vector2.one;
        vr.offsetMin = Vector2.zero; vr.offsetMax = Vector2.zero;
        viewport.AddComponent<Mask>().showMaskGraphic = false;

        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        var cr = Rect(content);
        cr.anchorMin = new Vector2(0f, 1f); cr.anchorMax = new Vector2(1f, 1f);
        cr.pivot = new Vector2(0.5f, 1f);
        cr.sizeDelta = new Vector2(0f, 34f);

        var item = new GameObject("Item", typeof(RectTransform), typeof(Toggle));
        item.transform.SetParent(content.transform, false);
        var ir = Rect(item);
        ir.anchorMin = new Vector2(0f, 0.5f); ir.anchorMax = new Vector2(1f, 0.5f);
        ir.sizeDelta = new Vector2(0f, 34f);

        var itemBg = Panel("Item Background", item.transform, new Color(0, 0, 0, 0));
        var ibr = Rect(itemBg);
        ibr.anchorMin = Vector2.zero; ibr.anchorMax = Vector2.one;
        ibr.offsetMin = Vector2.zero; ibr.offsetMax = Vector2.zero;

        var itemCheck = Panel("Item Checkmark", item.transform, Amber);
        itemCheck.GetComponent<Image>().raycastTarget = false;
        var icr = Rect(itemCheck);
        icr.anchorMin = new Vector2(0f, 0f); icr.anchorMax = new Vector2(0f, 1f);
        icr.pivot = new Vector2(0f, 0.5f);
        icr.sizeDelta = new Vector2(2f, 0f);

        var itemLabel = Text("Item Label", item.transform, "Opção", 15f, TextHi, 6f,
                             TextAlignmentOptions.Left);
        itemLabel.enableWordWrapping = false;
        var ilr = itemLabel.rectTransform;
        ilr.anchorMin = Vector2.zero; ilr.anchorMax = Vector2.one;
        ilr.offsetMin = new Vector2(16f, 0f); ilr.offsetMax = new Vector2(-8f, 0f);

        var itemToggle = item.GetComponent<Toggle>();
        itemToggle.targetGraphic = itemBg.GetComponent<Image>();
        itemToggle.graphic = itemCheck.GetComponent<Image>();
        var icb = itemToggle.colors;
        icb.normalColor = new Color(0, 0, 0, 0);
        icb.highlightedColor = SurfaceHi;
        icb.selectedColor = SurfaceHi;
        icb.pressedColor = Surface;
        icb.fadeDuration = 0.08f;
        itemToggle.colors = icb;

        scroll.content = cr;
        scroll.viewport = vr;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 22f;

        template.SetActive(false);

        var dd = go.AddComponent<TMP_Dropdown>();
        dd.targetGraphic = go.GetComponent<Image>();
        dd.template = tr;
        dd.captionText = caption;
        dd.itemText = itemLabel;
        var dcb = dd.colors;
        dcb.normalColor = Color.white;
        dcb.highlightedColor = new Color(1.5f, 1.5f, 1.6f, 1f);
        dcb.pressedColor = new Color(0.85f, 0.85f, 0.9f, 1f);
        dcb.fadeDuration = 0.1f;
        dd.colors = dcb;

        return dd;
    }

    /// <summary>Coluna vertical de botões, alinhada à esquerda.</summary>
    public static GameObject Column(string name, Transform parent, float spacing)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var v = go.AddComponent<VerticalLayoutGroup>();
        v.spacing = spacing;
        v.childControlWidth = false; v.childControlHeight = false;
        v.childForceExpandWidth = false; v.childForceExpandHeight = false;
        v.childAlignment = TextAnchor.UpperLeft;
        var f = go.AddComponent<ContentSizeFitter>();
        f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        f.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        return go;
    }
}
