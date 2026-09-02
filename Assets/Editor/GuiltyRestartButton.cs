using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Cria o botão "Reiniciar investigação" na SampleScene e liga no UIController.
///
/// Clona o "Send Button" em vez de montar um botão do zero: assim herda fonte,
/// cor, tamanho e o lugar no VerticalLayoutGroup do painel, sem duplicar as
/// decisões de estilo que já estão no GuiltySceneSetupUI.
///
/// O botão nasce DESATIVADO — o UIController só o mostra no fim de jogo.
///
/// Rodar por: menu Guilty > UI - Criar Botão de Reiniciar.
/// </summary>
[InitializeOnLoad]
public static class GuiltyRestartButton
{
    private const string ScenePath  = "Assets/Scenes/SampleScene.unity";
    private const string SendName   = "Send Button";
    private const string RestartName = "Restart Button";
    private const string Label      = "Reiniciar investigação";

    private static string MarkerPath =>
        Path.Combine(Directory.GetParent(Application.dataPath).FullName, ".guilty_restart_btn_done");

    static GuiltyRestartButton()
    {
        if (File.Exists(MarkerPath)) return;
        EditorApplication.delayCall += () =>
        {
            if (File.Exists(MarkerPath)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.WriteAllText(MarkerPath, System.DateTime.Now.ToString("o"));
            try { Run(); }
            catch (System.Exception e) { Debug.LogError("[UI] auto-setup do botão falhou: " + e); }
        };
    }

    [MenuItem("Guilty/UI - Criar Botão de Reiniciar")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[UI] saia do Play mode antes.");
            return;
        }

        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var ui = Object.FindFirstObjectByType<UIController>();
        if (ui == null) { Debug.LogError("[UI] não achei o UIController na cena."); return; }

        // já existe?
        var restart = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .FirstOrDefault(t => t.name == RestartName);

        if (restart == null)
        {
            var send = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(t => t.name == SendName);
            if (send == null) { Debug.LogError("[UI] não achei o \"" + SendName + "\" para clonar."); return; }

            var clone = Object.Instantiate(send.gameObject, send.parent);
            clone.name = RestartName;
            clone.transform.SetSiblingIndex(send.GetSiblingIndex() + 1);
            restart = clone.transform;
            Debug.Log("[UI] botão clonado a partir do \"" + SendName + "\"");
        }

        // rótulo
        var label = restart.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.text = Label;
        var legacy = restart.GetComponentInChildren<Text>(true);
        if (legacy != null) legacy.text = Label;

        // o clone herdou o listener do Send Button no onClick persistente; limpa
        var btn = restart.GetComponent<Button>();
        if (btn != null)
        {
            for (int i = btn.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
                UnityEventTools_RemoveAt(btn.onClick, i);
            btn.interactable = true;
        }

        restart.gameObject.SetActive(false);   // o UIController mostra no fim de jogo

        // liga a referência no UIController
        var so = new SerializedObject(ui);
        var prop = so.FindProperty("restartButton");
        if (prop == null) { Debug.LogError("[UI] campo restartButton não existe no UIController."); return; }
        prop.objectReferenceValue = btn;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(ui);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log("[UI] \"" + RestartName + "\" pronto e ligado no UIController (inicia desativado).");
    }

    /// <summary>UnityEventTools vive no assembly de Editor; wrapper para não poluir o topo.</summary>
    private static void UnityEventTools_RemoveAt(UnityEngine.Events.UnityEvent evt, int index)
    {
        UnityEditor.Events.UnityEventTools.RemovePersistentListener(evt, index);
    }
}
