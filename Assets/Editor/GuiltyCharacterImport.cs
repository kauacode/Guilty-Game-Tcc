using UnityEditor;
using UnityEngine;

/// <summary>
/// Import settings dos personagens (Assets/Models/Characters/*.fbx).
///
/// Roda sozinho a cada (re)import — é o mecanismo idiomático da Unity para
/// travar configuração de import. Serve de TEMPLATE: toda animação futura do
/// detetive (cansaço, intimidação, soco na mesa, fumar) cai neste mesmo caminho
/// e herda estas configurações sem ninguém precisar clicar em nada.
///
/// Decisões e o porquê:
///
///   • Generic, não Humanoid.
///     Humanoid retarget passa a pose por muscle space e por um avatar padrão.
///     Este rig tem 12 bones e NENHUM bone de dedo — não casa com o esqueleto
///     humanoide, e a conversão distorceria a pose da mão, que foi validada no
///     Blender com folga de 2,69 mm até o tampo da mesa. Generic preserva as
///     transforms dos bones exatamente como saíram do Blender.
///
///   • Sem root motion (motionNodeName vazio).
///     A animação só mexe Hand.L; o bone raiz (Spine) é constante. Sem nó de
///     root motion o Animator não tem como deslocar o personagem — drift fica
///     impossível por construção, não por sorte.
///
///   • Root Transform "Based Upon: Original" (keepOriginal* = true).
///     Impede a Unity de re-ancorar a pose por conta própria entre loops.
///
///   • animationCompression = Off.
///     O clip tem 48 frames e três impactos de 3 frames cada. Keyframe reduction
///     suavizaria justamente a chegada seca na mesa, que é o que vende a batida.
///
///   • Materiais externos, busca recursiva pra cima.
///     Material.001.mat já existe em Models/Environment/Materials com as texturas
///     T_Detective_* ligadas pelo GuiltyArtPass. Reusa em vez de duplicar.
/// </summary>
public class GuiltyCharacterImport : AssetPostprocessor
{
    private const string CharacterDir = "Assets/Models/Characters/";

    private bool IsCharacter => assetPath.Replace('\\', '/').StartsWith(CharacterDir);

    private void OnPreprocessModel()
    {
        if (!IsCharacter) return;
        var mi = (ModelImporter)assetImporter;

        // ── escala: 1 unidade Blender = 1 unidade Unity = 1 m ──
        mi.globalScale        = 1f;
        mi.useFileScale       = true;
        mi.bakeAxisConversion = false;   // o FBX já saiu do Blender em Y-up / -Z-forward

        // ── rig ──
        mi.animationType      = ModelImporterAnimationType.Generic;
        mi.avatarSetup        = ModelImporterAvatarSetup.CreateFromThisModel;
        mi.skinWeights        = ModelImporterSkinWeights.Standard;
        mi.optimizeGameObjects = false;  // mantém a hierarquia de bones acessível
        mi.motionNodeName     = "";      // <None> — sem root motion, sem drift

        // ── malha ──
        mi.importBlendShapes  = false;   // o personagem não tem shape keys
        mi.isReadable         = false;
        mi.meshCompression    = ModelImporterMeshCompression.Off;
        mi.importNormals      = ModelImporterNormals.Import;
        mi.importTangents     = ModelImporterTangents.CalculateMikk;

        // ── materiais: reusar os que já existem no projeto ──
        // materialName TEM que ser BasedOnMaterialName. No default (BasedOnTextureName)
        // a Unity batiza o material pela textura — o detetive virou
        // "texture_pbr_20250901" e ganhou uma duplicata em vez de reusar Material.001.
        // Search = Everywhere porque Material.001.mat mora em
        // Models/Environment/Materials, que não está na cadeia de pastas acima do FBX,
        // então RecursiveUp não alcança.
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        mi.materialLocation   = ModelImporterMaterialLocation.External;
        mi.materialName       = ModelImporterMaterialName.BasedOnMaterialName;
        mi.materialSearch     = ModelImporterMaterialSearch.Everywhere;

        // ── animação ──
        mi.importAnimation      = true;
        mi.resampleCurves       = true;
        mi.animationCompression = ModelImporterAnimationCompression.Off;
    }

    private void OnPreprocessAnimation()
    {
        if (!IsCharacter) return;
        var mi = (ModelImporter)assetImporter;

        var clips = mi.clipAnimations;
        if (clips == null || clips.Length == 0) clips = mi.defaultClipAnimations;
        if (clips == null || clips.Length == 0) return;

        for (int i = 0; i < clips.Length; i++)
        {
            var c = clips[i];

            var desired = DesiredClipName(clips.Length, i);
            if (!string.IsNullOrEmpty(desired)) c.name = desired;

            c.loopTime = true;
            c.loopPose = false;   // f1 e f48 já são idênticos no Blender; não precisa loop pose

            // Root Transform: Based Upon = Original, em todos os eixos.
            // Nada de re-ancoragem automática entre ciclos.
            c.keepOriginalOrientation = true;
            c.keepOriginalPositionY   = true;
            c.keepOriginalPositionXZ  = true;
            c.lockRootRotation        = false;
            c.lockRootHeightY         = false;
            c.lockRootPositionXZ      = false;
            c.heightFromFeet          = false;
            c.cycleOffset             = 0f;

            clips[i] = c;
        }

        mi.clipAnimations = clips;
    }

    /// <summary>
    /// Nome do clip na Unity. O Blender nomeia o take pela CENA ("Scene"), não pela
    /// action — então o nome tem que vir daqui.
    ///
    /// Convenção para as próximas animações: exportar um FBX só de animação chamado
    ///     SK_Detective@Detective_Gesture_Smoke.fbx
    /// O que vem depois do '@' vira o nome do clip automaticamente, sem tocar neste
    /// script. O SK_Detective.fbx (malha + rig + clip piloto) fica no dicionário
    /// abaixo porque foi feito antes dessa convenção existir.
    /// </summary>
    private string DesiredClipName(int clipCount, int index)
    {
        var file = System.IO.Path.GetFileNameWithoutExtension(assetPath);

        int at = file.IndexOf('@');
        if (at >= 0 && at < file.Length - 1)
            return file.Substring(at + 1);

        string explicitName;
        if (ClipNameOverrides.TryGetValue(assetPath.Replace('\\', '/'), out explicitName)
            && clipCount == 1 && index == 0)
            return explicitName;

        return null;
    }

    private static readonly System.Collections.Generic.Dictionary<string, string> ClipNameOverrides =
        new System.Collections.Generic.Dictionary<string, string>
        {
            { "Assets/Models/Characters/SK_Detective.fbx", "Detective_Idle_FingerTap" },
        };
}
