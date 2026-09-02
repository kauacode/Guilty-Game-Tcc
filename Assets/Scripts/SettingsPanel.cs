using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tela de Configurações. Só liga controles ao GameSettings — nenhuma regra
/// mora aqui, o que permite usar este mesmo prefab no Menu e, depois, no Pause.
///
/// Abre e fecha com o mesmo fade curto (+ escala quase imperceptível) das outras
/// telas, em unscaledDeltaTime para funcionar também com o jogo pausado.
/// </summary>
public class SettingsPanel : MonoBehaviour
{
    [Header("Áudio")]
    [SerializeField] private Slider masterSlider;
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private TMP_Text masterValue;
    [SerializeField] private TMP_Text musicValue;
    [SerializeField] private TMP_Text sfxValue;
    [Tooltip("Aviso mostrado enquanto o jogo não tiver nenhum som — sem isto o " +
             "slider parece quebrado para quem testa.")]
    [SerializeField] private TMP_Text audioNotice;

    [Header("Vídeo")]
    [SerializeField] private TMP_Dropdown resolutionDropdown;
    [SerializeField] private Toggle fullscreenToggle;

    [Header("Navegação")]
    [SerializeField] private CanvasGroup group;
    [SerializeField] private float fadeDuration = 0.22f;

    private List<Vector2Int> resolutions;
    private bool wiring;

    private void Awake()
    {
        if (group == null) group = GetComponent<CanvasGroup>();
        HideInstant();
    }

    public void Open()
    {
        LoadIntoUI();
        gameObject.SetActive(true);
        group.blocksRaycasts = true;
        group.interactable = true;
        StopAllCoroutines();
        StartCoroutine(Fade(1f, true));
    }

    /// <summary>Fecha salvando — é o comportamento do botão Voltar.</summary>
    public void CloseAndSave()
    {
        GameSettings.Save();
        group.blocksRaycasts = false;
        group.interactable = false;
        StopAllCoroutines();
        StartCoroutine(Fade(0f, false));
    }

    private void HideInstant()
    {
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;
        gameObject.SetActive(false);
    }

    // ─────────────────────────── ligação com o estado ───────────────────────────

    private void LoadIntoUI()
    {
        GameSettings.Load();
        wiring = true;   // evita disparar os callbacks ao popular os controles

        if (masterSlider != null) { masterSlider.value = GameSettings.Master; }
        if (musicSlider  != null) { musicSlider.value  = GameSettings.Music;  }
        if (sfxSlider    != null) { sfxSlider.value    = GameSettings.Sfx;    }
        UpdateValueLabels();

        if (audioNotice != null)
        {
            // o aviso some sozinho no dia em que houver som no projeto
            bool silent = !GameSettings.HasMixer && FindAnyObjectByType<AudioSource>() == null;
            audioNotice.gameObject.SetActive(silent);
        }

        if (resolutionDropdown != null)
        {
            resolutions = GameSettings.AvailableResolutions();
            resolutionDropdown.ClearOptions();
            var labels = new List<string>();
            foreach (var r in resolutions) labels.Add($"{r.x} × {r.y}");
            resolutionDropdown.AddOptions(labels);

            var saved = GameSettings.SavedResolution;
            int idx = resolutions.FindIndex(r => r.x == saved.x && r.y == saved.y);
            if (idx < 0) idx = resolutions.FindIndex(r => r.x == Screen.width && r.y == Screen.height);
            resolutionDropdown.value = Mathf.Max(0, idx);
            resolutionDropdown.RefreshShownValue();
        }

        if (fullscreenToggle != null) fullscreenToggle.isOn = GameSettings.Fullscreen;

        wiring = false;
    }

    private void UpdateValueLabels()
    {
        if (masterValue != null) masterValue.text = Pct(GameSettings.Master);
        if (musicValue  != null) musicValue.text  = Pct(GameSettings.Music);
        if (sfxValue    != null) sfxValue.text    = Pct(GameSettings.Sfx);
    }

    private static string Pct(float v) => Mathf.RoundToInt(v * 100f) + "%";

    // callbacks ligados pelos controles no Inspector
    public void OnMasterChanged(float v) { if (wiring) return; GameSettings.SetMaster(v); UpdateValueLabels(); }
    public void OnMusicChanged (float v) { if (wiring) return; GameSettings.SetMusic(v);  UpdateValueLabels(); }
    public void OnSfxChanged   (float v) { if (wiring) return; GameSettings.SetSfx(v);    UpdateValueLabels(); }

    public void OnResolutionChanged(int index)
    {
        if (wiring || resolutions == null) return;
        if (index < 0 || index >= resolutions.Count) return;
        GameSettings.ApplyResolution(resolutions[index], GameSettings.Fullscreen);
    }

    public void OnFullscreenChanged(bool on)
    {
        if (wiring) return;
        GameSettings.SetFullscreen(on);
    }

    // ─────────────────────────────── transição ───────────────────────────────

    private IEnumerator Fade(float target, bool keepActive)
    {
        float from = group.alpha, t = 0f;
        var rect = transform as RectTransform;

        while (t < fadeDuration)
        {
            t += Time.unscaledDeltaTime;         // funciona com timeScale = 0
            float k = Mathf.Clamp01(t / fadeDuration);
            k = k * k * (3f - 2f * k);           // smoothstep, sem bounce
            group.alpha = Mathf.Lerp(from, target, k);
            if (rect != null)
            {
                float s = target > 0.5f ? Mathf.Lerp(0.985f, 1f, k) : Mathf.Lerp(1f, 0.985f, k);
                rect.localScale = new Vector3(s, s, 1f);
            }
            yield return null;
        }

        group.alpha = target;
        if (rect != null) rect.localScale = Vector3.one;
        if (!keepActive && target <= 0f) gameObject.SetActive(false);
    }
}
