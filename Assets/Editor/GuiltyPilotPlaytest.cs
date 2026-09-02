using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Fase 4 do piloto — entra em Play mode na cena de teste, captura os frames-chave
/// e volta para a cena que você tinha aberta.
///
/// Seguro por construção: se a cena aberta tiver alterações não salvas, o auto-run
/// NÃO roda (para não te fazer perder trabalho num prompt de "salvar?"). Nesse caso
/// use o menu quando quiser.
///
/// Rodar por: menu Guilty > Piloto - Playtest + Screenshots.
/// </summary>
[InitializeOnLoad]
public static class GuiltyPilotPlaytest
{
    private const string TestScenePath = "Assets/Scenes/AnimPilot_Detective.unity";
    private const string PendingKey    = "GuiltyPilot.PlaytestPending";
    private const string SetupKey      = "GuiltyPilot.PrevScenePath";

    private static string MarkerPath =>
        Path.Combine(Directory.GetParent(Application.dataPath).FullName, ".guilty_pilot_playtest_done");

    static GuiltyPilotPlaytest()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;

        if (File.Exists(MarkerPath)) return;
        _waitFrames = 0;
        EditorApplication.update -= WaitThenAutoRun;
        EditorApplication.update += WaitThenAutoRun;
    }

    private static int _waitFrames;

    /// <summary>
    /// Espera o setup terminar antes de entrar em Play mode.
    ///
    /// Setup e playtest são dois auto-runs no mesmo domain reload e a ordem entre
    /// eles NÃO é garantida. Quando o playtest ganhava, o setup caía dentro do Play
    /// mode e EditorSceneManager.NewScene falhava ("cannot be used during play mode").
    /// Em vez de assumir ordem, aqui a condição é checada a cada tick.
    /// </summary>
    private static void WaitThenAutoRun()
    {
        if (File.Exists(MarkerPath)) { EditorApplication.update -= WaitThenAutoRun; return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        // dá tempo dos imports e do setup assentarem
        if (++_waitFrames < 120) return;

        var setupDone = File.Exists(Path.Combine(
            Directory.GetParent(Application.dataPath).FullName, ".guilty_pilot_setup_done"));
        if (!setupDone) return;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TestScenePath) == null) return;

        var active = EditorSceneManager.GetActiveScene();
        if (active.isDirty)
        {
            EditorApplication.update -= WaitThenAutoRun;
            Debug.Log("[Piloto] cena aberta tem alterações não salvas — playtest automático " +
                      "pulado. Use Guilty > Piloto - Playtest + Screenshots.");
            return;
        }

        EditorApplication.update -= WaitThenAutoRun;
        File.WriteAllText(MarkerPath, System.DateTime.Now.ToString("o"));
        Run();
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
