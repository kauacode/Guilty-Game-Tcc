using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Filtro noir do jogo — color grading de pós-processamento.
///
/// ARQUITETURA, e por que não foi feito no volume que já existia:
///
///   • O GuiltyArtPass.SetupVolume() reescreve Tonemapping, ColorAdjustments, Bloom e
///     FilmGrain direto no SampleSceneProfile toda vez que a Fase 5 roda. Qualquer noir
///     colocado lá seria apagado no próximo art pass. Por isso o noir vive num profile
///     PRÓPRIO, num Volume de prioridade maior, que só sobrepõe o que precisa.
///
///   • A vinheta NÃO está aqui de propósito. O SuspicionVisualFeedback dirige
///     vignette.intensity todo frame pelo nível de suspeita; se este Volume
///     sobrescrevesse a vinheta com prioridade maior, o feedback de gameplay sumiria.
///     A base noir da vinheta virou o campo baseIntensity daquele script.
///
/// Reuso: é só arrastar o prefab PF_NoirVolume para qualquer cena, ou chamar
/// o menu com a cena aberta. Todos os valores ficam no NoirVolumeProfile.
///
/// Rodar por: menu Guilty > Noir - Aplicar Filtro na Cena Aberta.
/// </summary>
[InitializeOnLoad]
public static class GuiltyNoirGrade
{
    private static string MarkerPath =>
        Path.Combine(Directory.GetParent(Application.dataPath).FullName, ".guilty_noir_done");

    static GuiltyNoirGrade()
    {
        if (File.Exists(MarkerPath)) return;
        _frames = 0;
        EditorApplication.update -= AutoOnce;
        EditorApplication.update += AutoOnce;
    }

    private static int _frames;

    private static void AutoOnce()
    {
        if (File.Exists(MarkerPath)) { EditorApplication.update -= AutoOnce; return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (++_frames < 60) return;
        var active = EditorSceneManager.GetActiveScene();
        if (active.isDirty)
        {
            EditorApplication.update -= AutoOnce;
            Debug.Log("[Noir] cena com alterações não salvas — use Guilty > Noir - Aplicar Filtro na Cena Aberta.");
            return;
        }
        EditorApplication.update -= AutoOnce;
        File.WriteAllText(MarkerPath, System.DateTime.Now.ToString("o"));
        Run();
    }

    private const string ProfilePath = "Assets/Settings/NoirVolumeProfile.asset";
    private const string PrefabPath  = "Assets/Prefabs/PF_NoirVolume.prefab";
    private const string VolumeName  = "NoirVolume";
    private const int    Priority    = 10;   // acima do Global Volume (priority 0)

    [MenuItem("Guilty/Noir - Aplicar Filtro na Cena Aberta")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Noir] saia do Play mode antes de aplicar.");
            return;
        }

        var profile = BuildProfile();
        var scene   = EditorSceneManager.GetActiveScene();

        // ── Volume na cena (idempotente) ──
        var go = GameObject.Find(VolumeName);
        if (go == null)
        {
            go = new GameObject(VolumeName);
            UnityEditor.SceneManagement.EditorSceneManager.MoveGameObjectToScene(go, scene);
            Undo.RegisterCreatedObjectUndo(go, "Create NoirVolume");
        }
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        var vol = go.GetComponent<Volume>() ?? go.AddComponent<Volume>();
        vol.isGlobal      = true;
        vol.priority      = Priority;
        vol.weight        = 1f;
        vol.blendDistance = 0f;
        vol.sharedProfile = profile;

        EditorUtility.SetDirty(go);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        // ── prefab para reuso em outras cenas ──
        Directory.CreateDirectory(Path.GetDirectoryName(
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, PrefabPath)));
        PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);

        AssetDatabase.SaveAssets();
        Debug.Log("[Noir] filtro aplicado em " + scene.name +
                  "\n  profile: " + ProfilePath +
                  "\n  prefab:  " + PrefabPath +
                  "\n  volume:  " + VolumeName + " (priority " + Priority + ")");
    }

    /// <summary>Cria ou atualiza o NoirVolumeProfile. Todos os números do look estão aqui.</summary>
    private static VolumeProfile BuildProfile()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }

        // ── 1. base: escurece, tira cor, endurece o contraste ──
        // A cena JÁ é escura por iluminação. Tirar exposição aqui apagava mesa, cinzeiro
        // e telefone no preto — e a vinheta de runtime ainda soma por cima. Então o noir
        // vem do contraste e da cor, não de escurecer: a exposição sobe de leve.
        var ca = GetOrAdd<ColorAdjustments>(profile);
        Set(ca.postExposure,  0.10f);
        Set(ca.contrast,      20f);     // substitui o 18 do art pass, sem esmagar
        Set(ca.saturation,   -28f);     // dessatura, mas ainda sobra cor na pele
        Set(ca.colorFilter, new Color(0.94f, 0.965f, 1.0f, 1f));  // veio frio quase imperceptível

        // ── 2. o coração do look: sombra fria, luz da luminária quente ──
        // ShadowsMidtonesHighlights separa por faixa de luminância, então a
        // luminária (highlights) continua quente enquanto o resto esfria.
        // É por isso que este efeito existe aqui em vez de só White Balance,
        // que esfriaria a cena inteira, luminária junto.
        var smh = GetOrAdd<ShadowsMidtonesHighlights>(profile);
        // o w é offset de luminância: +0.05 nas sombras levanta o piso do preto,
        // preservando informação nos cantos sem clarear a cena inteira
        Set(smh.shadows,    new Vector4(0.86f, 0.96f, 1.15f,  0.05f)); // azul-ciano
        Set(smh.midtones,   new Vector4(0.98f, 1.00f, 1.02f,  0.02f)); // quase neutro
        Set(smh.highlights, new Vector4(1.12f, 1.03f, 0.88f,  0.02f)); // âmbar da luminária
        Set(smh.shadowsStart,    0.00f);
        Set(smh.shadowsEnd,      0.36f);
        Set(smh.highlightsStart, 0.52f);
        Set(smh.highlightsEnd,   1.00f);

        // ── 3. temperatura global levemente fria (discreta, para não matar o âmbar) ──
        var wb = GetOrAdd<WhiteBalance>(profile);
        Set(wb.temperature, -8f);
        Set(wb.tint,         4f);   // pitada de verde, o "institucional" do noir

        // ── 4. tonemapping: ACES, mesma escolha que o art pass já fez ──
        var tm = GetOrAdd<Tonemapping>(profile);
        Set(tm.mode, TonemappingMode.ACES);

        // ── 5. grain cinematográfico sutil ──
        var fg = GetOrAdd<FilmGrain>(profile);
        Set(fg.type,      FilmGrainLookup.Thin1);
        Set(fg.intensity, 0.28f);
        Set(fg.response,  0.75f);   // quase some nas altas luzes

        // Vignette de propósito AUSENTE: quem manda nela é o SuspicionVisualFeedback.

        EditorUtility.SetDirty(profile);
        return profile;
    }

    /// <summary>
    /// Pega ou cria o override no profile.
    ///
    /// O AddObjectToAsset é obrigatório: VolumeProfile.Add&lt;T&gt;() só cria o componente
    /// em memória. Sem anexá-lo ao asset, o profile é salvo com "components: []" e o
    /// filtro simplesmente não existe depois do reload — foi o que aconteceu na
    /// primeira tentativa.
    /// </summary>
    private static T GetOrAdd<T>(VolumeProfile p) where T : VolumeComponent
    {
        if (p.TryGet(out T existing)) return existing;

        var c = p.Add<T>(true);
        c.hideFlags = HideFlags.HideInHierarchy;
        if (AssetDatabase.Contains(p) && !AssetDatabase.Contains(c))
            AssetDatabase.AddObjectToAsset(c, p);
        return c;
    }

    private static void Set(FloatParameter p, float v)          { p.overrideState = true; p.value = v; }
    private static void Set(ClampedFloatParameter p, float v)   { p.overrideState = true; p.value = v; }
    private static void Set(ColorParameter p, Color v)          { p.overrideState = true; p.value = v; }
    private static void Set(Vector4Parameter p, Vector4 v)      { p.overrideState = true; p.value = v; }
    private static void Set(TonemappingModeParameter p, TonemappingMode v)  { p.overrideState = true; p.value = v; }
    private static void Set(FilmGrainLookupParameter p, FilmGrainLookup v)  { p.overrideState = true; p.value = v; }
}
