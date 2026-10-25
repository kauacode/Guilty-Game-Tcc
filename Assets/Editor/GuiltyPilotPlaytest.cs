using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Fase 4 do piloto — entra em Play mode na cena de teste, captura os frames-chave
/// e volta para a cena que você tinha aberta.
///
/// Rodar por: menu Guilty > Piloto - Playtest + Screenshots.
/// </summary>
[InitializeOnLoad]
public static class GuiltyPilotPlaytest
{
    private const string TestScenePath = "Assets/Scenes/AnimPilot_Detective.unity";
    private const string PendingKey    = "GuiltyPilot.PlaytestPending";
    private const string SetupKey      = "GuiltyPilot.PrevScenePath";

    // O InitializeOnLoad fica SÓ para religar o playModeStateChanged a cada
    // domain reload: o Run do menu depende dele para capturar e voltar à cena
    // anterior. O auto-run (marcador .guilty_*_done) foi tirado: o marcador não
    // vai para o Git, então todo clone novo entrava em Play e salvava a cena.
    static GuiltyPilotPlaytest()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("Guilty/Piloto - Playtest + Screenshots")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Piloto] já está em Play mode.");
            return;
        }

        var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(TestScenePath);
        if (scene == null)
        {
            Debug.LogError("[Piloto] não achei " + TestScenePath +
                           ". Rode Guilty > Piloto - Setup Animação Detetive antes.");
            return;
        }

        // lembra para onde voltar
        SessionState.SetString(SetupKey, EditorSceneManager.GetActiveScene().path);

        var s = EditorSceneManager.OpenScene(TestScenePath, OpenSceneMode.Single);

        // garante o componente de captura no personagem
        var added = false;
        foreach (var root in s.GetRootGameObjects())
        {
            var animator = root.GetComponentInChildren<Animator>();
            if (animator == null) continue;
            if (animator.GetComponent<DetectivePilotCapture>() == null)
            {
                animator.gameObject.AddComponent<DetectivePilotCapture>();
                added = true;
            }
            break;
        }
        if (added) EditorSceneManager.SaveScene(s);

        SessionState.SetBool(PendingKey, true);
        Debug.Log("[Piloto] entrando em Play mode para capturar...");
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredEditMode) return;
        if (!SessionState.GetBool(PendingKey, false)) return;
        SessionState.SetBool(PendingKey, false);

        var prev = SessionState.GetString(SetupKey, "");
        if (!string.IsNullOrEmpty(prev) && prev != TestScenePath &&
            AssetDatabase.LoadAssetAtPath<SceneAsset>(prev) != null)
        {
            EditorSceneManager.OpenScene(prev, OpenSceneMode.Single);
            Debug.Log("[Piloto] playtest terminou, voltei para " + prev);
        }
        else
        {
            Debug.Log("[Piloto] playtest terminou.");
        }
    }
}
