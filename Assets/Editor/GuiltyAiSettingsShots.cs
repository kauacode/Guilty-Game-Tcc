using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Screenshots da página Detetive (IA) nos estados que o jogador vai ver,
/// sem entrar em Play mode nem precisar do backend: os textos são preenchidos
/// à mão com o que o AiSettingsPage escreveria. Saem em PilotScreens/ia_*.png.
///
/// A cena NÃO é salva — nada disso persiste.
/// Rodar por: menu Guilty > UI - Screenshots Detetive (IA).
/// </summary>
public static class GuiltyAiSettingsShots
{
    private const string MenuScene = "Assets/Scenes/MainMenu.unity";

    [MenuItem("Guilty/UI - Screenshots Detetive (IA)")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        var menu = EditorSceneManager.OpenScene(MenuScene, OpenSceneMode.Single);
        var panel = menu.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<SettingsPanel>(true)).First();
        var page = panel.GetComponentInChildren<AiSettingsPage>(true);
        var so = new SerializedObject(page);
        T Ref<T>(string field) where T : Object => (T)so.FindProperty(field).objectReferenceValue;

        var dropdown = Ref<TMP_Dropdown>("providerDropdown");
        var desc     = Ref<TMP_Text>("descriptionText");
        var keyRow   = Ref<GameObject>("keyRow");
        var keyInput = Ref<TMP_InputField>("keyInput");
        var modelRow = Ref<GameObject>("modelRow");
        var label    = Ref<TMP_Text>("modelButtonLabel");
        var bar      = Ref<GameObject>("progressBar");
        var fill     = Ref<RectTransform>("progressFill");
        var status   = Ref<TMP_Text>("statusText");

        var amber = new Color(0.78f, 0.53f, 0.16f, 1f);
        var muted = new Color(0.62f, 0.61f, 0.58f, 1f);
        const string qwenDesc = "Roda no seu computador, offline e de graça. Respostas em 20-30s.\n" +
                                "Download de 2,1 GB · requer 8 GB de RAM.";

        void ShowPage(bool ai)
        {
            GuiltyFlowShots.ToggleSettings(menu, true);
            panel.transform.Find("Content/Page_Geral").gameObject.SetActive(!ai);
            page.gameObject.SetActive(ai);
        }

        void Local(string button, float progress, string text, Color color)
        {
            dropdown.captionText.text = "Qwen 2.5 3B (local)";
            desc.text = qwenDesc;
            keyRow.SetActive(false);
            modelRow.SetActive(true);
            bar.SetActive(progress >= 0f);
            fill.anchorMax = new Vector2(Mathf.Max(0f, progress), 1f);
            label.text = button;
            status.text = text;
            status.color = color;
        }

        GuiltyFlowShots.Capture(menu, "ia_0_config_geral", () => ShowPage(false));

        GuiltyFlowShots.Capture(menu, "ia_1_gemini", () =>
        {
            ShowPage(true);
            dropdown.captionText.text = "Gemini 2.5 Flash (nuvem)";
            desc.text = "Mais rápido e esperto. Precisa de internet e de uma chave gratuita do Google AI Studio.";
            keyRow.SetActive(true);
            modelRow.SetActive(false);
            bar.SetActive(false);
            keyInput.textComponent.text = new string('•', 39);
            keyInput.placeholder.gameObject.SetActive(false);
            status.text = "Chave válida. O Gemini está pronto para interrogar.";
            status.color = amber;
        });

        GuiltyFlowShots.Capture(menu, "ia_2_qwen_nao_baixado", () =>
        {
            ShowPage(true);
            Local("BAIXAR (2,1 GB)", -1f, "Ainda não baixado. Baixe uma vez e jogue offline.", muted);
        });

        GuiltyFlowShots.Capture(menu, "ia_3_qwen_baixando", () =>
        {
            ShowPage(true);
            Local("CANCELAR", 0.45f, "Baixando... 45% (0,9 GB de 2,1 GB)", muted);
        });

        GuiltyFlowShots.Capture(menu, "ia_4_qwen_pronto", () =>
        {
            ShowPage(true);
            Local("APAGAR MODELO", -1f,
                  "Modelo baixado e pronto. O detetive vai rodar no seu computador, sem internet.", amber);
        });

        Debug.Log("[IA] screenshots em PilotScreens/ia_*.png");
    }
}
