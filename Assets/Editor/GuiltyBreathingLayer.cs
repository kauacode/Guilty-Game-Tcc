using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Camada de respiração — adiciona uma Layer ADITIVA ao AC_Detective.
///
/// Como funciona a soma aditiva na Unity:
///   resultado = pose da layer base + (pose do clip − pose de referência do clip)
/// A pose de referência é o frame 1 do clip. Como o frame 1 da respiração é
/// exatamente a pose base (delta zero), a camada não desloca nada em repouso e
/// só soma o movimento do tórax por cima do que a base estiver tocando.
///
/// A layer NÃO usa AvatarMask de propósito: mascarar não impede herança de
/// transform (filho segue o pai). O isolamento foi resolvido no Blender, com
/// UpperArm.L/R e Thigh.L/R contra-rotacionados para deslocamento zero no mundo.
///
/// TEMPLATE para futuras animações de base (piscar, micro-ajuste de peso):
/// mesma receita — layer própria, Additive, weight 1, um único estado default,
/// clip em loop cujo primeiro frame É a pose base.
///
/// Rodar por: menu Guilty > Respiração - Criar Layer Aditiva.
/// </summary>
[InitializeOnLoad]
public static class GuiltyBreathingLayer
{
    private const string ControllerPath = "Assets/Animation/Detective/AC_Detective.controller";
    private const string BreathFbx      = "Assets/Models/Characters/SK_Detective@Detective_Idle_Breathing.fbx";
    private const string LayerName      = "Breathing";
    private const string StateName      = "Breathing";

    private static string MarkerPath =>
        Path.Combine(Directory.GetParent(Application.dataPath).FullName, ".guilty_breathing_done");

    static GuiltyBreathingLayer()
    {
        if (File.Exists(MarkerPath)) return;
        // delayCall em vez de EditorApplication.update: o update quase não tica
        // enquanto o editor está sem foco, e o auto-run ficava pendurado.
        EditorApplication.delayCall += Auto;
    }

    private static void Auto()
    {
        if (File.Exists(MarkerPath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.WriteAllText(MarkerPath, System.DateTime.Now.ToString("o"));
        try { Run(); }
        catch (System.Exception e) { Debug.LogError("[Respiração] auto-run falhou: " + e); }
    }

    [MenuItem("Guilty/Respiração - Criar Layer Aditiva")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Respiração] saia do Play mode antes.");
            return;
        }

        // O AssetDatabase V2 versiona por hash de conteúdo: mudar o postprocessor
        // não invalida o artefato do FBX. Sem este ForceUpdate o clip pode vir com
        // as settings do import anterior.
        AssetDatabase.ImportAsset(BreathFbx,
            ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

        var clip = AssetDatabase.LoadAllAssetsAtPath(BreathFbx)
            .OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        if (clip == null)
        {
            Debug.LogError("[Respiração] o FBX não trouxe AnimationClip: " + BreathFbx);
            return;
        }

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError("[Respiração] não achei " + ControllerPath);
            return;
        }

        // ── layer aditiva (idempotente) ──
        int idx = System.Array.FindIndex(controller.layers, l => l.name == LayerName);
        AnimatorControllerLayer layer;
        if (idx < 0)
        {
            controller.AddLayer(LayerName);
            idx = controller.layers.Length - 1;
        }
        layer = controller.layers[idx];

        var sm = layer.stateMachine;
        var state = sm.states.FirstOrDefault(s => s.state.name == StateName).state
                    ?? sm.AddState(StateName, new Vector3(260, 0, 0));
        state.motion = clip;
        state.writeDefaultValues = true;
        sm.defaultState = state;

        // AnimatorControllerLayer é struct-like: precisa reatribuir o array
        var layers = controller.layers;
        layers[idx].name          = LayerName;
        layers[idx].blendingMode  = AnimatorLayerBlendingMode.Additive;
        layers[idx].defaultWeight = 1f;
        layers[idx].iKPass        = false;
        layers[idx].avatarMask    = null;   // ver comentário no topo
        controller.layers = layers;

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // ── relatório do que a engine leu ──
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        var bindings = AnimationUtility.GetCurveBindings(clip);
        var paths = new System.Collections.Generic.HashSet<string>(bindings.Select(b => b.path));
        var final = controller.layers[idx];

        var r = new StringBuilder();
        r.AppendLine("{");
        r.AppendLine("  \"clip\": \"" + clip.name + "\",");
        r.AppendLine("  \"clipLengthSeconds\": " + clip.length + ",");
        r.AppendLine("  \"clipFrameRate\": " + clip.frameRate + ",");
        r.AppendLine("  \"clipFrames\": " + Mathf.RoundToInt(clip.length * clip.frameRate) + ",");
        r.AppendLine("  \"respiracoesPorMinuto\": " + (60f / clip.length).ToString("F1") + ",");
        r.AppendLine("  \"loopTime\": " + (settings.loopTime ? "true" : "false") + ",");
        r.AppendLine("  \"hasRootCurves\": " + (clip.hasRootCurves ? "true" : "false") + ",");
        r.AppendLine("  \"hasMotionCurves\": " + (clip.hasMotionCurves ? "true" : "false") + ",");
        r.AppendLine("  \"curveBindings\": " + bindings.Length + ",");
        r.AppendLine("  \"animatedPaths\": " + paths.Count + ",");
        r.AppendLine("  \"bonesAnimados\": \"" + string.Join(";", paths.OrderBy(p => p)) + "\",");
        r.AppendLine("  \"layerName\": \"" + final.name + "\",");
        r.AppendLine("  \"layerIndex\": " + idx + ",");
        r.AppendLine("  \"blendingMode\": \"" + final.blendingMode + "\",");
        r.AppendLine("  \"defaultWeight\": " + final.defaultWeight + ",");
        r.AppendLine("  \"avatarMask\": \"" + (final.avatarMask == null ? "none" : final.avatarMask.name) + "\",");
        r.AppendLine("  \"todasAsLayers\": \"" +
            string.Join(" | ", controller.layers.Select(l => l.name + ":" + l.blendingMode + ":w" + l.defaultWeight)) + "\"");
        r.AppendLine("}");
        File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                                       "BreathingLayerReport.json"), r.ToString());

        Debug.Log("[Respiração] layer aditiva criada: " + LayerName +
                  " (index " + idx + ", " + final.blendingMode + ", weight " + final.defaultWeight + ")" +
                  "\n  clip: " + clip.name + " — " + clip.length.ToString("F3") + "s @ " + clip.frameRate + "fps");
    }
}
