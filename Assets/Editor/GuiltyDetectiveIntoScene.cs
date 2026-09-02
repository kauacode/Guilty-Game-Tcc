using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Coloca o detetive ANIMADO na cena do jogo.
///
/// Duas coisas precisam acontecer juntas, senão ficam dois detetives sobrepostos:
///   1. instanciar o PF_Detective (rig + Animator + disparador);
///   2. desativar o Detective_Posed, a malha estática que vem embutida no FBX da sala.
///
/// Também remove o DetectivePilotCapture se ele aparecer aqui: esse componente é da
/// cena de piloto e SAI DO PLAY MODE sozinho depois de tirar os screenshots — na cena
/// do jogo ele encerraria a partida em poucos segundos.
///
/// Rodar por: menu Guilty > Piloto - Colocar Detetive Animado na SampleScene.
/// </summary>
[InitializeOnLoad]
public static class GuiltyDetectiveIntoScene
{
    private const string ScenePath  = "Assets/Scenes/SampleScene.unity";
    private const string PrefabPath = "Assets/Prefabs/Characters/PF_Detective.prefab";

    private static string MarkerPath =>
        Path.Combine(Directory.GetParent(Application.dataPath).FullName, ".guilty_detective_in_scene_done");

    static GuiltyDetectiveIntoScene()
    {
        if (File.Exists(MarkerPath)) return;
        _frames = 0;
        EditorApplication.update -= WaitThenRun;
        EditorApplication.update += WaitThenRun;
    }

    private static int _frames;

    private static void WaitThenRun()
    {
        if (File.Exists(MarkerPath)) { EditorApplication.update -= WaitThenRun; return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (++_frames < 90) return;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null) return;

        var active = EditorSceneManager.GetActiveScene();
        if (active.isDirty)
        {
            EditorApplication.update -= WaitThenRun;
            Debug.Log("[Piloto] SampleScene tem alterações não salvas — integração automática pulada. " +
                      "Use Guilty > Piloto - Colocar Detetive Animado na SampleScene.");
            return;
        }

        EditorApplication.update -= WaitThenRun;
        File.WriteAllText(MarkerPath, System.DateTime.Now.ToString("o"));
        Run();
    }

    [MenuItem("Guilty/Piloto - Colocar Detetive Animado na SampleScene")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Piloto] em Play mode — saia do Play antes de integrar.");
            return;
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) { Debug.LogError("[Piloto] não achei " + PrefabPath); return; }

        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var log = new List<string>();

        // ── o animado já está aqui? (idempotente) ──
        GameObject animated = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.GetComponentInChildren<DetectiveIdleTrigger>(true) != null) { animated = root; break; }
        }

        if (animated == null)
        {
            animated = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            animated.name = "PF_Detective";
            animated.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            log.Add("PF_Detective instanciado em (0,0,0)");
        }
        else
        {
            log.Add("PF_Detective já estava na cena — mantido");
        }

        // ── o componente de captura não pode ficar aqui ──
        foreach (var cap in animated.GetComponentsInChildren<DetectivePilotCapture>(true))
        {
            Object.DestroyImmediate(cap, true);
            log.Add("DetectivePilotCapture REMOVIDO (encerraria o Play mode sozinho)");
        }

        // ── desativa a malha estática que vem no FBX da sala ──
        int disabled = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root == animated) continue;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "Detective_Posed" && t.name != "Detective") continue;
                if (t.GetComponentInParent<DetectiveIdleTrigger>() != null) continue; // nunca o animado
                if (!t.gameObject.activeSelf) continue;
                t.gameObject.SetActive(false);
                disabled++;
                log.Add("desativado: " + GetPath(t) + " (clone estático)");
            }
        }
        if (disabled == 0) log.Add("nenhuma malha estática ativa encontrada (já estava desativada?)");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        var animator = animated.GetComponentInChildren<Animator>(true);
        var trigger  = animated.GetComponentInChildren<DetectiveIdleTrigger>(true);
        var report = new System.Text.StringBuilder();
        report.AppendLine("{");
        report.AppendLine("  \"scene\": \"" + ScenePath + "\",");
        report.AppendLine("  \"animatedPresent\": true,");
        report.AppendLine("  \"animatorController\": \"" +
            (animator != null && animator.runtimeAnimatorController != null
                ? animator.runtimeAnimatorController.name : "NULL") + "\",");
        report.AppendLine("  \"applyRootMotion\": " +
            (animator != null && animator.applyRootMotion ? "true" : "false") + ",");
        report.AppendLine("  \"idleTriggerPresent\": " + (trigger != null ? "true" : "false") + ",");
        report.AppendLine("  \"pilotCaptureRemoved\": true,");
        report.AppendLine("  \"staticMeshesDisabled\": " + disabled + ",");
        report.AppendLine("  \"actions\": \"" + string.Join(" | ", log) + "\"");
        report.AppendLine("}");
        File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                                       "SceneIntegrationReport.json"), report.ToString());

        Debug.Log("[Piloto] detetive animado integrado na SampleScene.\n" + string.Join("\n", log));
    }

    private static string GetPath(Transform t)
    {
        var p = t.name;
        while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }
}
