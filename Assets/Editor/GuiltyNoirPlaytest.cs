using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Entra em Play mode na SampleScene, captura o noir com vinheta e grain, e sai.
/// Limpa o objeto temporário de captura ao voltar para edit mode, para não deixar
/// resíduo de validação na cena do jogo.
///
/// Rodar por: menu Guilty > Noir - Validar em Play Mode.
/// </summary>
[InitializeOnLoad]
public static class GuiltyNoirPlaytest
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string TempName  = "__NoirCapture_TEMP";
    private const string PendingKey = "GuiltyNoir.Pending";

    // O InitializeOnLoad fica SÓ para religar o playModeStateChanged a cada
    // domain reload: o Run do menu depende dele para limpar a cena ao sair do
    // Play. O auto-run (marcador .guilty_*_done) foi tirado: o marcador não vai
    // para o Git, então todo clone novo entrava em Play e salvava a SampleScene.
    static GuiltyNoirPlaytest()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("Guilty/Noir - Validar em Play Mode")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Noir] já está em Play mode.");
            return;
        }

        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        if (GameObject.Find(TempName) == null)
        {
            var go = new GameObject(TempName);
            EditorSceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<NoirPlayCapture>();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        SessionState.SetBool(PendingKey, true);
        Debug.Log("[Noir] entrando em Play mode para validar...");
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredEditMode) return;
        if (!SessionState.GetBool(PendingKey, false)) return;
        SessionState.SetBool(PendingKey, false);

        // remove o resíduo de validação da cena do jogo
        var go = GameObject.Find(TempName);
        if (go != null)
        {
            Object.DestroyImmediate(go);
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Noir] objeto temporário de captura removido da cena.");
        }
    }
}
