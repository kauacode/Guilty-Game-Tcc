using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Piloto de animação do detetive — monta o mínimo necessário para testar em Play mode.
///
/// O que faz:
///   1. Animator Controller com Idle + FingerTap e um trigger.
///   2. Prefab do personagem com Animator + o disparador provisório.
///   3. Cena de teste isolada (aditiva — NÃO mexe na cena que você tem aberta).
///   4. Relatório JSON na raiz do projeto com o que a engine realmente importou,
///      para conferir sem depender de olhar no Inspector.
///
/// Os import settings do FBX NÃO estão aqui — ficam no GuiltyCharacterImport.cs,
/// que roda sozinho no import. Este script só cria assets.
///
/// Rodar por: menu Guilty > Piloto - Setup Animação Detetive.
/// Não salva a cena aberta automaticamente, igual ao GuiltyArtPass.
/// </summary>
public static class GuiltyCharacterSetup
{
    private const string FbxPath        = "Assets/Models/Characters/SK_Detective.fbx";
    private const string RoomFbxPath    = "Assets/Models/Environment/GUILTY_InterrogationRoom.fbx";
    private const string AnimDir        = "Assets/Animation/Detective";
    private const string ControllerPath = AnimDir + "/AC_Detective.controller";
    private const string PrefabDir      = "Assets/Prefabs/Characters";
    private const string PrefabPath     = PrefabDir + "/PF_Detective.prefab";
    private const string TestScenePath  = "Assets/Scenes/AnimPilot_Detective.unity";
    private const string ClipName       = "Detective_Idle_FingerTap";
    private const string TriggerParam   = "FingerTap";

    private static string MarkerPath =>
        Path.Combine(Directory.GetParent(Application.dataPath).FullName, ".guilty_pilot_setup_done");

    /// <summary>
    /// Roda o setup UMA vez sozinho, no primeiro domain reload depois que o FBX
    /// existir. Existe só para o piloto não depender de você achar o menu.
    /// Apague o arquivo .guilty_pilot_setup_done na raiz do projeto para permitir
    /// que rode de novo, ou use o menu, que roda sempre.
    /// </summary>
    [InitializeOnLoadMethod]
    private static void AutoRunOnce()
    {
        if (File.Exists(MarkerPath)) return;

        EditorApplication.delayCall += () =>
        {
            if (File.Exists(MarkerPath)) return;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath) == null) return; // FBX ainda não importou
            File.WriteAllText(MarkerPath, System.DateTime.Now.ToString("o"));
            try { Run(true); }
            catch (System.Exception e) { Debug.LogError("[Piloto Detetive] auto-setup falhou: " + e); }
        };
    }

    [MenuItem("Guilty/Piloto - Setup Animação Detetive")]
    public static void RunFromMenu() { Run(false); }

    public static void Run(bool silent)
    {
        // EditorSceneManager.NewScene é proibido em Play mode. Sem esta guarda o
        // setup e o playtest se atropelam e a cena de teste não é criada.
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Piloto Detetive] em Play mode — setup adiado. Saia do Play e rode de novo.");
            return;
        }

        var log = new List<string>();
        var report = new StringBuilder();

        // ── 0. reimportar o FBX à força ────────────────────────────────
        // O AssetDatabase V2 versiona por HASH DE CONTEÚDO, não por data. Mexer no
        // GuiltyCharacterImport.cs não invalida o artefato do FBX, então sem este
        // ForceUpdate o modelo continua com as settings do import anterior — foi
        // exatamente o que travou o nome do clip e o material durante o piloto.
        AssetDatabase.ImportAsset(FbxPath,
            ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

        // ── 1. achar o clip dentro do FBX ──────────────────────────────
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        if (model == null)
        {
            Fail(silent, "Não achei " + FbxPath + ".\nRode o export no Blender primeiro.");
            return;
        }

        var clip = AssetDatabase.LoadAllAssetsAtPath(FbxPath)
            .OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));

        if (clip == null)
        {
            Fail(silent, "O FBX importou mas não trouxe AnimationClip.\n" +
                         "Confira Import Settings > Animation > Import Animation.");
            return;
        }
        log.Add("clip encontrado: " + clip.name);

        EnsureFolder(AnimDir);
        EnsureFolder(PrefabDir);

        // ── 2. Animator Controller ─────────────────────────────────────
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        if (!controller.parameters.Any(p => p.name == TriggerParam))
            controller.AddParameter(TriggerParam, AnimatorControllerParameterType.Trigger);

        var sm = controller.layers[0].stateMachine;

        // Idle é um estado vazio de propósito: a bind pose do rig JÁ é a pose
        // sentada corrigida (foi rebaseada no Blender). Estado sem motion =
        // personagem sentado parado, sem custo e sem clip extra.
        var idle = FindOrAddState(sm, "Idle", new Vector3(260, 0, 0));
        idle.motion = null;
        sm.defaultState = idle;

        var tap = FindOrAddState(sm, "FingerTap", new Vector3(260, 90, 0));
        tap.motion = clip;
        tap.writeDefaultValues = true;

        // Idle -> FingerTap no trigger
        RemoveTransitions(idle);
        var toTap = idle.AddTransition(tap);
        toTap.hasExitTime = false;
        toTap.duration    = 0.12f;   // blend curto: f1 do clip já É a pose de repouso
        toTap.AddCondition(AnimatorConditionMode.If, 0f, TriggerParam);

        // FingerTap -> Idle ao terminar o ciclo
        RemoveTransitions(tap);
        var toIdle = tap.AddTransition(idle);
        toIdle.hasExitTime = true;
        toIdle.exitTime    = 1f;     // f48, que é idêntico a f1
        toIdle.duration    = 0.12f;

        EditorUtility.SetDirty(controller);
        log.Add("controller: " + ControllerPath);

        // ── 3. Prefab ──────────────────────────────────────────────────
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
        inst.name = "PF_Detective";
        inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        var animator = inst.GetComponent<Animator>() ?? inst.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;   // 2a trava contra drift
        animator.cullingMode     = AnimatorCullingMode.AlwaysAnimate;

        if (inst.GetComponent<DetectiveIdleTrigger>() == null)
            inst.AddComponent<DetectiveIdleTrigger>();

        PrefabUtility.SaveAsPrefabAsset(inst, PrefabPath);
        Object.DestroyImmediate(inst);
        log.Add("prefab: " + PrefabPath);

        // ── 4. cena de teste, aditiva (não toca na cena aberta) ────────
        string sceneMsg;
        try
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            go.transform.position = Vector3.zero;

            // A sala precisa estar aqui: sem a mesa não dá pra validar a folga da mão,
            // que é o critério principal do piloto.
            var room = AssetDatabase.LoadAssetAtPath<GameObject>(RoomFbxPath);
            if (room != null)
            {
                var roomGo = (GameObject)PrefabUtility.InstantiatePrefab(room, scene);
                roomGo.transform.position = Vector3.zero;

                // O FBX da sala carrega Detective_Posed — a malha estática com a pose
                // assada, que o pipeline antigo exportava no lugar do rig. Na cena de
                // teste ela congelaria por cima do personagem animado.
                foreach (var t in roomGo.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "Detective_Posed" || t.name == "Detective")
                    {
                        t.gameObject.SetActive(false);
                        log.Add("desativado na cena de teste: " + t.name + " (clone estático da sala)");
                    }
                }
            }

            // Câmera na mesma posição da Camera_Suspect_POV do Blender.
            // Conversão do export (axis_forward=-Z, axis_up=Y): Blender (x,y,z) -> Unity (x,z,-y).
            //   posição  Blender (0, -1.1, 1.2)  -> Unity (0, 1.2, 1.1)
            //   direção  Blender +Y (olhar)      -> Unity -Z
            // Câmera da Unity olha pelo +Z local, então precisa girar 180° em Y.
            var camGo = new GameObject("Cam_SuspectPOV");
            SceneManager.MoveGameObjectToScene(camGo, scene);
            var cam = camGo.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(new Vector3(0f, 1.2f, 1.1f),
                                                 Quaternion.Euler(0f, 180f, 0f));
            cam.fieldOfView = 39.6f;   // 50 mm em sensor 36 mm, igual ao Blender
            camGo.tag = "MainCamera";

            // A sala é fechada: luz direcional não entra. Point light sobre a mesa,
            // só para enxergar a geometria na validação.
            var lightGo = new GameObject("Key_Light");
            SceneManager.MoveGameObjectToScene(lightGo, scene);
            var l = lightGo.AddComponent<Light>();
            l.type      = LightType.Point;
            l.intensity = 3.5f;
            l.range     = 8f;
            lightGo.transform.position = new Vector3(0f, 2.1f, 0f);

            RenderSettings.ambientMode      = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight     = new Color(0.35f, 0.35f, 0.38f);

            EnsureFolder("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, TestScenePath);
            EditorSceneManager.CloseScene(scene, true);
            sceneMsg = TestScenePath;
        }
        catch (System.Exception e)
        {
            sceneMsg = "FALHOU: " + e.Message;
        }
        log.Add("cena de teste: " + sceneMsg);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // ── 5. relatório do que a engine realmente importou ────────────
        var mi = (ModelImporter)AssetImporter.GetAtPath(FbxPath);
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        var bindings = AnimationUtility.GetCurveBindings(clip);
        var animatedPaths = new HashSet<string>(bindings.Select(b => b.path));
        var skin = model.GetComponentInChildren<SkinnedMeshRenderer>();

        report.AppendLine("{");
        report.AppendLine("  \"fbx\": \"" + FbxPath + "\",");
        report.AppendLine("  \"animationType\": \"" + mi.animationType + "\",");
        report.AppendLine("  \"avatarValid\": " + J(mi.sourceAvatar != null || model.GetComponent<Animator>()?.avatar != null) + ",");
        report.AppendLine("  \"globalScale\": " + mi.globalScale + ",");
        report.AppendLine("  \"useFileScale\": " + J(mi.useFileScale) + ",");
        report.AppendLine("  \"motionNodeName\": \"" + mi.motionNodeName + "\",");
        report.AppendLine("  \"animationCompression\": \"" + mi.animationCompression + "\",");
        report.AppendLine("  \"clipName\": \"" + clip.name + "\",");
        report.AppendLine("  \"clipLengthSeconds\": " + clip.length + ",");
        report.AppendLine("  \"clipFrameRate\": " + clip.frameRate + ",");
        report.AppendLine("  \"clipFrames\": " + Mathf.RoundToInt(clip.length * clip.frameRate) + ",");
        report.AppendLine("  \"loopTime\": " + J(settings.loopTime) + ",");
        report.AppendLine("  \"loopBlend\": " + J(settings.loopBlend) + ",");
        report.AppendLine("  \"keepOriginalOrientation\": " + J(settings.keepOriginalOrientation) + ",");
        report.AppendLine("  \"keepOriginalPositionY\": " + J(settings.keepOriginalPositionY) + ",");
        report.AppendLine("  \"keepOriginalPositionXZ\": " + J(settings.keepOriginalPositionXZ) + ",");
        report.AppendLine("  \"hasRootCurves\": " + J(clip.hasRootCurves) + ",");
        report.AppendLine("  \"hasMotionCurves\": " + J(clip.hasMotionCurves) + ",");
        report.AppendLine("  \"curveBindings\": " + bindings.Length + ",");
        report.AppendLine("  \"animatedTransformPaths\": " + animatedPaths.Count + ",");
        report.AppendLine("  \"bonesInSkin\": " + (skin != null ? skin.bones.Length : -1) + ",");
        report.AppendLine("  \"vertices\": " + (skin != null && skin.sharedMesh != null ? skin.sharedMesh.vertexCount : -1) + ",");
        report.AppendLine("  \"materials\": \"" + (skin != null ? string.Join(";", skin.sharedMaterials.Select(m => m == null ? "NULL" : m.name)) : "") + "\",");
        report.AppendLine("  \"controller\": \"" + ControllerPath + "\",");
        report.AppendLine("  \"controllerStates\": \"" + string.Join(";", sm.states.Select(s => s.state.name)) + "\",");
        report.AppendLine("  \"controllerParams\": \"" + string.Join(";", controller.parameters.Select(p => p.name + ":" + p.type)) + "\",");
        report.AppendLine("  \"prefab\": \"" + PrefabPath + "\",");
        report.AppendLine("  \"testScene\": \"" + sceneMsg + "\"");
        report.AppendLine("}");

        var outPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                                   "AnimPipelineReport.json");
        File.WriteAllText(outPath, report.ToString());

        Debug.Log("[Piloto Detetive] setup pronto.\n" + string.Join("\n", log) +
                  "\nrelatório: " + outPath);
        if (!silent)
            EditorUtility.DisplayDialog("Piloto Detetive",
                "Setup pronto.\n\n" + string.Join("\n", log) +
                "\n\nAbra " + TestScenePath + " e aperte Play.", "OK");
    }

    private static void Fail(bool silent, string msg)
    {
        Debug.LogError("[Piloto Detetive] " + msg);
        if (!silent) EditorUtility.DisplayDialog("Piloto Detetive", msg, "OK");
    }

    private static string J(bool b) { return b ? "true" : "false"; }

    private static AnimatorState FindOrAddState(AnimatorStateMachine sm, string name, Vector3 pos)
    {
        foreach (var c in sm.states)
            if (c.state.name == name) return c.state;
        return sm.AddState(name, pos);
    }

    private static void RemoveTransitions(AnimatorState s)
    {
        foreach (var t in s.transitions.ToArray()) s.RemoveTransition(t);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parts = path.Split('/');
        var cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            var next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }
}
