using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Fase 5 — Art Pass: traz para o Unity a revisão de direção de arte feita no Blender.
///
/// O que NÃO atravessa o FBX e por isso é reconstruído aqui:
///   • materiais procedurais  -> texturas assadas no Blender + materiais URP/Lit criados aqui
///   • luzes                  -> recriadas a partir dos marcadores MARK_* que vêm no FBX
///   • Volume Scatter (haze)  -> fog do URP + cone FX_LightShaft com shader aditivo
///   • espelho falso          -> Reflection Probe
///   • AgX + contraste        -> Tonemapping/Color Adjustments no Volume da cena
///
/// Os valores vêm de RoomMaterials.json, gerado pelo export_to_unity.py no Blender.
/// Rodar por: menu Guilty > Fase 5 - Art Pass (Blender -> Unity).
/// Não salva a cena automaticamente — revise e salve com Ctrl+S.
/// </summary>
public static class GuiltyArtPass
{
    private const string FbxPath      = "Assets/Models/Environment/GUILTY_InterrogationRoom.fbx";
    private const string ManifestPath = "Assets/Models/Environment/RoomMaterials.json";
    private const string TextureDir   = "Assets/Models/Environment/Textures";
    private const string MaterialDir  = "Assets/Models/Environment/Materials";
    private const string ShaftShader  = "Guilty/LightShaftAdditive";
    private const string MirrorShader = "Guilty/TwoWayMirror";
    private const string RoomInstanceName = "GUILTY_InterrogationRoom";

    // ────────────────────────────────────────────────────────────────────
    // Modelo do manifesto (espelha o JSON escrito pelo Blender)
    // ────────────────────────────────────────────────────────────────────
    [Serializable]
    public class MatSpec
    {
        public string name;
        public float[] baseColorSRGB;   // já convertido de linear p/ sRGB no Blender
        public float metallic;
        public float roughness;
        public float[] emissionLinear;  // cor linear * strength (propriedade HDR, sem conversão)
        public string albedoMap;
        public string normalMap;
        public string metalSmoothMap;
        public bool transparent;
        public bool doubleSided;
        public bool shaft;              // usa o shader de facho em vez de URP/Lit
        public bool mirror;             // usa o shader de reflexo planar
    }

    [Serializable]
    public class LightSpec
    {
        public string name;
        public string marker;           // Empty MARK_* no FBX que dá a posição
        public string markerTarget;     // Empty de alvo; a luz faz LookAt nele.
                                        // Dois pontos em vez de uma rotação: assim não
                                        // dependo de acertar a conversão Z-up -> Y-up.
        public string type;             // Spot | Point
        public float[] colorSRGB;
        public float intensity;
        public float range;
        public float spotAngle;
        public float innerSpotAngle;
        public bool shadows;
        public string note;
    }

    [Serializable]
    public class RoomManifest
    {
        public string generated;
        public MatSpec[] materials;
        public LightSpec[] lights;
    }

    // ────────────────────────────────────────────────────────────────────
    [MenuItem("Guilty/Fase 5 - Art Pass (Blender -> Unity)")]
    public static void RunAll()
    {
        RoomManifest manifest = LoadManifest();
        if (manifest == null) return;

        // Sequencial de propósito: as texturas precisam estar reimportadas com os
        // settings certos (normal map, linear, alpha preservado) ANTES de os
        // materiais as referenciarem, e os materiais precisam existir antes de o
        // importer do FBX sair procurando por nome.
        ConfigureTextureImporters();
        AssetDatabase.Refresh();

        BuildMaterials(manifest);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        ConfigureModelImporter();
        ConfigurePipelineAsset();
        SetupScene(manifest);

        AssetDatabase.SaveAssets();   // persiste materiais, volume profile e URP asset

        Debug.Log("[ArtPass] Concluído. Revise a cena e salve com Ctrl+S.\n" +
                  "As intensidades de luz são um ponto de partida convertido do Blender (Watts não " +
                  "têm equivalente exato em URP) — calibre no Game view.");
    }

    private static RoomManifest LoadManifest()
    {
        string full = Path.Combine(Directory.GetCurrentDirectory(), ManifestPath);
        if (!File.Exists(full))
        {
            Debug.LogError($"[ArtPass] Manifesto não encontrado em {ManifestPath}. " +
                           "Rode export_to_unity.py no Blender primeiro.");
            return null;
        }

        RoomManifest m = JsonUtility.FromJson<RoomManifest>(File.ReadAllText(full));
        Debug.Log($"[ArtPass] Manifesto de {m.generated}: {m.materials.Length} materiais, {m.lights.Length} luzes.");
        return m;
    }

    // ────────────────────────────────────────────────────────────────────
    // 1. Import das texturas assadas
    // ────────────────────────────────────────────────────────────────────
    [MenuItem("Guilty/Art Pass - Passo 1  Importar texturas")]
    public static void ConfigureTextureImporters()
    {
        if (!Directory.Exists(TextureDir))
        {
            Debug.LogWarning($"[ArtPass] {TextureDir} não existe — nenhuma textura assada encontrada.");
            return;
        }

        int n = 0;
        foreach (string path in Directory.GetFiles(TextureDir, "*.png"))
        {
            string assetPath = path.Replace(Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar, "")
                                   .Replace('\\', '/');
            var ti = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (ti == null) continue;

            bool isNormal = assetPath.EndsWith("_Normal.png", StringComparison.OrdinalIgnoreCase);
            bool isData   = assetPath.EndsWith("_MetalSmooth.png", StringComparison.OrdinalIgnoreCase)
                         || assetPath.EndsWith("_Rough.png", StringComparison.OrdinalIgnoreCase);

            ti.textureType        = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture        = !isNormal && !isData;   // só o albedo é sRGB
            ti.alphaIsTransparency = false;                  // o alpha do MetalSmooth é smoothness, não transparência
            ti.alphaSource        = assetPath.EndsWith("_MetalSmooth.png", StringComparison.OrdinalIgnoreCase)
                                    ? TextureImporterAlphaSource.FromInput
                                    : TextureImporterAlphaSource.None;
            ti.mipmapEnabled      = true;
            ti.wrapMode           = TextureWrapMode.Clamp;   // atlas assado, não tileável
            ti.filterMode         = FilterMode.Trilinear;
            ti.anisoLevel         = 4;
            ti.maxTextureSize     = 4096;

            var settings = ti.GetDefaultPlatformTextureSettings();
            // MetalSmooth precisa de alpha íntegro -> formato com alpha de alta qualidade
            settings.format = assetPath.EndsWith("_MetalSmooth.png", StringComparison.OrdinalIgnoreCase)
                              ? TextureImporterFormat.Automatic
                              : TextureImporterFormat.Automatic;
            settings.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SetPlatformTextureSettings(settings);

            ti.SaveAndReimport();
            n++;
        }

        Debug.Log($"[ArtPass] Passo 1: {n} texturas configuradas (normal maps marcados, dados em linear).");
    }

    // ────────────────────────────────────────────────────────────────────
    // 2. Materiais URP/Lit
    // ────────────────────────────────────────────────────────────────────
    private static void BuildMaterials(RoomManifest manifest)
    {
        if (!Directory.Exists(MaterialDir))
        {
            Directory.CreateDirectory(MaterialDir);
            AssetDatabase.Refresh();
        }

        Shader lit    = Shader.Find("Universal Render Pipeline/Lit");
        Shader shaft  = Shader.Find(ShaftShader);
        Shader mirror = Shader.Find(MirrorShader);
        if (lit == null)
        {
            Debug.LogError("[ArtPass] Shader URP/Lit não encontrado. O projeto está com URP ativo?");
            return;
        }

        int created = 0, updated = 0;
        foreach (MatSpec spec in manifest.materials)
        {
            string path = $"{MaterialDir}/{spec.name}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = mat == null;

            Shader target = spec.shaft  && shaft  != null ? shaft
                          : spec.mirror && mirror != null ? mirror
                          : lit;
            if (isNew)
            {
                mat = new Material(target);
                AssetDatabase.CreateAsset(mat, path);
                created++;
            }
            else
            {
                if (mat.shader != target) mat.shader = target;
                updated++;
            }

            if (spec.shaft)
            {
                ApplyShaftMaterial(mat, spec);
                continue;
            }

            if (spec.mirror)
            {
                // _ReflectionTex é preenchida em runtime pelo PlanarMirrorReflection.
                mat.SetColor("_Tint",      ToColor(spec.baseColorSRGB));
                mat.SetColor("_BaseColor", new Color(0.012f, 0.014f, 0.013f, 1f));
                // 1.0 e não 0.75: o reflexo passa pelo mesmo tonemapping ACES do frame,
                // e o Contrast +18 do grading empurra os valores médios-baixos ainda
                // mais para baixo. Um corte de 25% em linear vira bem mais que isso em
                // tela, e o reflexo lia como se não tivesse iluminação nenhuma.
                mat.SetFloat("_Strength",  1.0f);
                mat.SetFloat("_Fresnel",   0.9f);
                mat.SetFloat("_Grime",     0.06f);
                EditorUtility.SetDirty(mat);
                continue;
            }

            ApplyLitMaterial(mat, spec);
            EditorUtility.SetDirty(mat);
        }

        Debug.Log($"[ArtPass] Passo 2: {created} materiais criados, {updated} atualizados em {MaterialDir}.");
    }

    private static void ApplyLitMaterial(Material mat, MatSpec spec)
    {
        // Cor base: o JSON já traz sRGB porque o Unity trata propriedades Color
        // como gamma e converte para linear na hora de renderizar (projeto em Linear).
        mat.SetColor("_BaseColor", ToColor(spec.baseColorSRGB));
        mat.SetFloat("_Metallic", spec.metallic);
        mat.SetFloat("_Smoothness", 1f - spec.roughness);

        Texture2D albedo = LoadTex(spec.albedoMap);
        if (albedo != null) mat.SetTexture("_BaseMap", albedo);

        Texture2D normal = LoadTex(spec.normalMap);
        if (normal != null)
        {
            mat.SetTexture("_BumpMap", normal);
            mat.SetFloat("_BumpScale", 1f);
            mat.EnableKeyword("_NORMALMAP");
        }
        else
        {
            mat.DisableKeyword("_NORMALMAP");
        }

        Texture2D ms = LoadTex(spec.metalSmoothMap);
        if (ms != null)
        {
            mat.SetTexture("_MetallicGlossMap", ms);
            mat.SetFloat("_SmoothnessTextureChannel", 0f);  // smoothness no alpha do metallic map
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
        }
        else
        {
            mat.DisableKeyword("_METALLICSPECGLOSSMAP");
        }

        // Emissão: propriedade HDR, o Unity não aplica conversão de gamma nela.
        Color emis = new Color(spec.emissionLinear[0], spec.emissionLinear[1], spec.emissionLinear[2]);
        bool hasEmission = emis.maxColorComponent > 0.0005f;
        mat.SetColor("_EmissionColor", emis);
        if (hasEmission)
        {
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else
        {
            mat.DisableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }

        // Opaco x transparente
        if (spec.transparent)
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
        else
        {
            mat.SetFloat("_Surface", 0f);
            mat.SetInt("_SrcBlend", (int)BlendMode.One);
            mat.SetInt("_DstBlend", (int)BlendMode.Zero);
            mat.SetInt("_ZWrite", 1);
            mat.renderQueue = (int)RenderQueue.Geometry;
            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }

        mat.SetFloat("_Cull", spec.doubleSided ? (float)CullMode.Off : (float)CullMode.Back);
        mat.doubleSidedGI = spec.doubleSided;
    }

    private static void ApplyShaftMaterial(Material mat, MatSpec spec)
    {
        mat.SetColor("_Color", ToColor(spec.baseColorSRGB));
        // 0.55 lavava o frame inteiro: o cone tem 1,10 m de raio na base e a câmera
        // do suspeito olha bem através dele, então as duas paredes somavam em cima
        // de tudo. 0.18 deixa o facho legível sem virar neblina.
        mat.SetFloat("_Intensity", 0.18f);
        mat.SetFloat("_EdgeSoftness", 3.2f);
        mat.SetFloat("_BottomFade", 0.70f);
        mat.SetFloat("_TopBoost", 0.70f);
        mat.SetFloat("_DepthFade", 0.55f);
        EditorUtility.SetDirty(mat);
    }

    private static Texture2D LoadTex(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        return AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureDir}/{name}.png");
    }

    private static Color ToColor(float[] c)
    {
        if (c == null || c.Length < 3) return Color.magenta;
        return new Color(c[0], c[1], c[2], c.Length > 3 ? c[3] : 1f);
    }

    // ────────────────────────────────────────────────────────────────────
    // 3. Importer do FBX — achar os materiais por nome
    // ────────────────────────────────────────────────────────────────────
    private static void ConfigureModelImporter()
    {
        var mi = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
        if (mi == null)
        {
            Debug.LogError($"[ArtPass] FBX não encontrado em {FbxPath}.");
            return;
        }

        mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        mi.materialLocation   = ModelImporterMaterialLocation.External;
        mi.materialSearch     = ModelImporterMaterialSearch.Everywhere;
        mi.importNormals      = ModelImporterNormals.Import;
        mi.importTangents     = ModelImporterTangents.CalculateMikk;   // normal maps precisam de tangentes
        mi.importLights       = false;                                  // as luzes são recriadas aqui
        mi.importCameras      = false;
        mi.SaveAndReimport();

        Debug.Log("[ArtPass] Passo 3: importer do FBX ajustado (materiais externos por nome, tangentes MikkTSpace).");
    }

    // ────────────────────────────────────────────────────────────────────
    // 4. URP asset — a sala usa 6 luzes adicionais, o limite padrão é 4
    // ────────────────────────────────────────────────────────────────────
    private static void ConfigurePipelineAsset()
    {
        var urp = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
        if (urp == null)
        {
            Debug.LogWarning("[ArtPass] URP asset ativo não encontrado — pulei o ajuste de limite de luzes.");
            return;
        }

        SerializedObject so = new SerializedObject(urp);
        SetIfLower(so, "m_AdditionalLightsPerObjectLimit", 8);
        SerializedProperty grading = so.FindProperty("m_ColorGradingMode");
        if (grading != null && grading.intValue == 0)
        {
            grading.intValue = 1;   // HDR: necessário para tonemapping e emissão se comportarem
            Debug.Log("[ArtPass] Color Grading Mode: LDR -> HDR.");
        }
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(urp);

        Debug.Log("[ArtPass] Passo 4: URP asset ajustado (limite de luzes adicionais >= 8, grading HDR).");
    }

    private static void SetIfLower(SerializedObject so, string prop, int value)
    {
        SerializedProperty p = so.FindProperty(prop);
        if (p != null && p.intValue < value)
        {
            p.intValue = value;
            Debug.Log($"[ArtPass] {prop}: -> {value}");
        }
    }

    // ────────────────────────────────────────────────────────────────────
    // 5. Cena — luzes, probe, facho, fog, câmera, colisor
    // ────────────────────────────────────────────────────────────────────
    [MenuItem("Guilty/Art Pass - Passo 5  Montar cena")]
    public static void SetupSceneMenu()
    {
        RoomManifest m = LoadManifest();
        if (m != null) SetupScene(m);
    }

    private static void SetupScene(RoomManifest manifest)
    {
        GameObject room = GameObject.Find(RoomInstanceName);
        if (room == null)
        {
            GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            if (fbx == null) { Debug.LogError("[ArtPass] FBX não carregou."); return; }
            room = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            room.name = RoomInstanceName;
            room.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Undo.RegisterCreatedObjectUndo(room, "Instantiate Room");
            Debug.Log("[ArtPass] Sala instanciada.");
        }

        Dictionary<string, Transform> markers = CollectMarkers(room.transform);
        Debug.Log($"[ArtPass] Marcadores MARK_* encontrados no FBX: {markers.Count}");

        SetupLights(manifest, markers);
        SetupLightShaft(room.transform);
        SetupMirrorReflection(room.transform);
        SetupMirrorProbe(room.transform, markers);
        SetupFog();
        SetupCamera(markers, room.transform);
        SetupRoomCollider(room);
        SetupVolume();
        DisableStrayDirectionalLight();
    }

    private static Dictionary<string, Transform> CollectMarkers(Transform root)
    {
        var dict = new Dictionary<string, Transform>();
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name.StartsWith("MARK_") && !dict.ContainsKey(t.name)) dict[t.name] = t;
        }
        return dict;
    }

    private static void SetupLights(RoomManifest manifest, Dictionary<string, Transform> markers)
    {
        GameObject group = GameObject.Find("Lighting_Interrogation");
        if (group == null)
        {
            group = new GameObject("Lighting_Interrogation");
            Undo.RegisterCreatedObjectUndo(group, "Create Lighting group");
        }

        foreach (LightSpec spec in manifest.lights)
        {
            GameObject go = GameObject.Find(spec.name);
            if (go == null)
            {
                go = new GameObject(spec.name);
                go.AddComponent<Light>();
                Undo.RegisterCreatedObjectUndo(go, $"Create {spec.name}");
            }
            if (go.GetComponent<Light>() == null) go.AddComponent<Light>();
            go.transform.SetParent(group.transform, true);

            Light l = go.GetComponent<Light>();
            Undo.RecordObject(l, "Configure light");
            Undo.RecordObject(go.transform, "Place light");

            l.type      = spec.type == "Spot" ? LightType.Spot : LightType.Point;
            l.color     = ToColor(spec.colorSRGB);
            l.intensity = spec.intensity;
            l.range     = spec.range;
            l.shadows   = spec.shadows ? LightShadows.Soft : LightShadows.None;
            l.shadowBias = 0.03f;
            l.shadowNormalBias = 0.15f;
            l.renderMode = LightRenderMode.ForcePixel;

            if (l.type == LightType.Spot)
            {
                l.spotAngle      = spec.spotAngle;
                l.innerSpotAngle = spec.innerSpotAngle;
            }

            if (markers.TryGetValue(spec.marker, out Transform mk))
            {
                go.transform.position = mk.position;

                // Spot precisa apontar: mira no Empty de alvo que veio junto no FBX.
                if (!string.IsNullOrEmpty(spec.markerTarget) &&
                    markers.TryGetValue(spec.markerTarget, out Transform tgt))
                {
                    Vector3 dir = tgt.position - mk.position;
                    if (dir.sqrMagnitude > 1e-6f)
                        go.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
                }
                else if (l.type == LightType.Spot)
                {
                    Debug.LogWarning($"[ArtPass] '{spec.name}' é Spot mas o alvo " +
                                     $"'{spec.markerTarget}' não veio no FBX — direção não definida.");
                }
            }
            else
            {
                Debug.LogWarning($"[ArtPass] Marcador {spec.marker} não veio no FBX — " +
                                 $"'{spec.name}' ficou na posição atual. Reexporte do Blender.");
            }
        }

        Debug.Log($"[ArtPass] {manifest.lights.Length} luzes posicionadas pelos marcadores do FBX.");
    }

    private static void SetupLightShaft(Transform room)
    {
        Transform shaft = FindDeep(room, "FX_LightShaft");
        if (shaft == null)
        {
            Debug.LogWarning("[ArtPass] FX_LightShaft não veio no FBX — o facho volumétrico não foi montado.");
            return;
        }

        var mr = shaft.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            // o cone é um efeito: não projeta nem recebe sombra, e não entra no lightmap
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows    = false;
            mr.lightProbeUsage   = LightProbeUsage.Off;
            mr.staticShadowCaster = false;
            EditorUtility.SetDirty(mr);
        }
        GameObjectUtility.SetStaticEditorFlags(shaft.gameObject, 0);
        Debug.Log("[ArtPass] FX_LightShaft configurado (sem sombras, sem lightmap).");
    }

    private static void SetupMirrorReflection(Transform room)
    {
        Transform mirror = FindDeep(room, "RoomV2_TwoWayMirror");
        if (mirror == null)
        {
            Debug.LogWarning("[ArtPass] RoomV2_TwoWayMirror não encontrado — reflexo planar não montado.");
            return;
        }

        var pmr = mirror.GetComponent<PlanarMirrorReflection>();
        if (pmr == null)
        {
            pmr = Undo.AddComponent<PlanarMirrorReflection>(mirror.gameObject);
            Debug.Log("[ArtPass] PlanarMirrorReflection adicionado ao espelho. " +
                      "URP não tem SSR e Reflection Probe é cubemap de um ponto — nenhum dos dois " +
                      "faz espelho plano. O reflexo agora vem de uma câmera espelhada.");
        }
        else
        {
            Debug.Log("[ArtPass] PlanarMirrorReflection já estava no espelho.");
        }

        // O espelho não deve aparecer no próprio reflexo.
        var mr = mirror.GetComponent<MeshRenderer>();
        if (mr != null) mr.shadowCastingMode = ShadowCastingMode.Off;
    }

    private static void SetupMirrorProbe(Transform room, Dictionary<string, Transform> markers)
    {
        Vector3 pos;
        if (markers.TryGetValue("MARK_ReflectionProbe", out Transform mk)) pos = mk.position;
        else
        {
            Transform mirror = FindDeep(room, "RoomV2_TwoWayMirror");
            if (mirror == null)
            {
                Debug.LogWarning("[ArtPass] Espelho não encontrado — Reflection Probe não criado.");
                return;
            }
            pos = mirror.position;
        }

        GameObject go = GameObject.Find("ReflectionProbe_Mirror");
        if (go == null)
        {
            go = new GameObject("ReflectionProbe_Mirror");
            go.AddComponent<ReflectionProbe>();
            Undo.RegisterCreatedObjectUndo(go, "Create Reflection Probe");
        }
        if (go.GetComponent<ReflectionProbe>() == null) go.AddComponent<ReflectionProbe>();

        ReflectionProbe p = go.GetComponent<ReflectionProbe>();
        Undo.RecordObject(p, "Configure probe");
        go.transform.position = pos;

        // No EEVEE o espelho vinha de screen-space + probe. Aqui o probe faz o trabalho todo:
        // captura da posição do vidro para que o reflexo mostre a sala, não o vazio.
        p.mode              = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
        p.refreshMode       = UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting;
        p.timeSlicingMode   = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.IndividualFaces;
        p.resolution        = 256;
        p.size              = new Vector3(4.4f, 2.8f, 5.0f);
        p.nearClipPlane     = 0.05f;
        p.farClipPlane      = 20f;
        p.hdr               = true;
        p.shadowDistance    = 20f;
        p.cullingMask       = ~0;
        p.RenderProbe();

        Debug.Log($"[ArtPass] Reflection Probe do espelho em {pos}. Modo Realtime/ViaScripting — " +
                  "chame RenderProbe() quando a sala mudar, ou troque para Baked antes do build.");
    }

    private static void SetupFog()
    {
        // Substitui o Volume Scatter do World do Blender (density 0.011, levemente quente).
        RenderSettings.fog          = true;
        RenderSettings.fogMode      = FogMode.ExponentialSquared;
        RenderSettings.fogColor     = new Color(0.055f, 0.062f, 0.075f, 1f);  // frio, quase preto
        RenderSettings.fogDensity   = 0.035f;
        RenderSettings.ambientMode  = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.035f, 0.042f, 0.055f, 1f);  // sala fechada e fria
        RenderSettings.reflectionIntensity = 0.65f;
        Debug.Log("[ArtPass] Fog exponencial fria + ambiente escuro (equivalente do haze do EEVEE).");
    }

    private static void SetupCamera(Dictionary<string, Transform> markers, Transform room)
    {
        Camera cam = Camera.main;
        if (cam == null) { Debug.LogWarning("[ArtPass] Main Camera não encontrada."); return; }

        Undo.RecordObject(cam.transform, "Reposition camera");
        Undo.RecordObject(cam, "Camera settings");

        if (markers.TryGetValue("MARK_Camera_SuspectPOV", out Transform mk))
        {
            cam.transform.position = mk.position;
            if (markers.TryGetValue("MARK_Camera_SuspectPOV_T", out Transform tgt))
                cam.transform.rotation = Quaternion.LookRotation(
                    (tgt.position - mk.position).normalized, Vector3.up);
        }
        else
        {
            Transform s = FindDeep(room, "PFB_Chair_Suspect");
            Transform d = FindDeep(room, "PFB_Chair_Detective");
            if (s != null && d != null)
            {
                cam.transform.position = s.position + Vector3.up * 1.26f;
                cam.transform.LookAt(d.position + Vector3.up * 1.22f);
            }
        }

        cam.nearClipPlane = 0.05f;
        cam.farClipPlane  = 30f;
        cam.fieldOfView   = 45f;   // ~35 mm em sensor de 36 mm, mesma lente dos renders de revisão

        var data = cam.GetUniversalAdditionalCameraData();
        if (data != null)
        {
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.requiresDepthTexture = true;   // o shader do facho precisa
        }

        Debug.Log($"[ArtPass] Main Camera no POV do suspeito, FOV 45, post-processing ligado.");
    }

    private static void SetupRoomCollider(GameObject room)
    {
        Renderer[] rs = room.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return;

        Bounds b = rs[0].bounds;
        foreach (Renderer r in rs) b.Encapsulate(r.bounds);

        GameObject go = GameObject.Find("RoomCollision");
        if (go == null)
        {
            go = new GameObject("RoomCollision");
            go.transform.SetParent(room.transform);
            go.AddComponent<BoxCollider>();
            Undo.RegisterCreatedObjectUndo(go, "Create RoomCollision");
        }
        if (go.GetComponent<BoxCollider>() == null) go.AddComponent<BoxCollider>();

        BoxCollider bc = go.GetComponent<BoxCollider>();
        Undo.RecordObject(bc, "Resize collider");
        Undo.RecordObject(go.transform, "Move collider");
        go.transform.position = b.center;
        bc.center = Vector3.zero;
        bc.size   = b.size;

        Debug.Log($"[ArtPass] RoomCollision atualizado para a sala menor: tamanho {b.size}.");
    }

    private static void SetupVolume()
    {
        GameObject go = GameObject.Find("Global Volume");
        if (go == null)
        {
            go = new GameObject("Global Volume");
            go.AddComponent<Volume>();
            Undo.RegisterCreatedObjectUndo(go, "Create Global Volume");
        }

        Volume v = go.GetComponent<Volume>();
        if (v == null) v = go.AddComponent<Volume>();
        v.isGlobal = true;

        // sharedProfile, não profile: o getter .profile instancia uma CÓPIA em runtime,
        // e as alterações não chegariam ao asset em disco.
        VolumeProfile profile = v.sharedProfile;
        if (profile == null)
        {
            Debug.LogWarning("[ArtPass] Global Volume sem profile atribuído — " +
                             "crie um VolumeProfile, atribua ao Global Volume e rode de novo.");
            return;
        }

        // Repara entradas nulas deixadas por componentes que foram criados só em
        // memória numa execução anterior — é o que fazia Volume.profile estourar.
        int removed = profile.components.RemoveAll(c => c == null);
        if (removed > 0)
            Debug.Log($"[ArtPass] {removed} componente(s) órfão(s) removido(s) do Volume Profile.");

        // Aproxima o AgX + Medium High Contrast usado no Blender.
        Tonemapping tm = GetOrAdd<Tonemapping>(profile);
        tm.mode.overrideState = true;
        tm.mode.value = TonemappingMode.ACES;   // rolloff de highlight parecido com o AgX

        ColorAdjustments ca = GetOrAdd<ColorAdjustments>(profile);
        ca.postExposure.overrideState = true; ca.postExposure.value = 0.15f;
        ca.contrast.overrideState     = true; ca.contrast.value     = 18f;
        ca.saturation.overrideState   = true; ca.saturation.value   = -8f;
        ca.colorFilter.overrideState  = true; ca.colorFilter.value  = new Color(1f, 0.985f, 0.97f);

        // Threshold alto de propósito: só a lâmpada, o display e o LED devem florescer.
        // Com 1.1 a mesa iluminada também entrava e contribuía para o aspecto leitoso.
        Bloom bl = GetOrAdd<Bloom>(profile);
        bl.intensity.overrideState = true; bl.intensity.value = 0.22f;
        bl.threshold.overrideState = true; bl.threshold.value = 1.45f;
        bl.scatter.overrideState   = true; bl.scatter.value   = 0.62f;

        // VIGNETTE: de propósito não mexo aqui.
        // SuspicionVisualFeedback.cs é dono dela em runtime — zera a intensidade no
        // Awake e a dirige pelo nível de suspeita. Qualquer valor que eu fixasse seria
        // sobrescrito no primeiro frame, e mexer no overrideState brigaria com o jogo.
        // O escurecimento de canto do noir vem da luz e do fog, não da vinheta.

        FilmGrain fg = GetOrAdd<FilmGrain>(profile);
        fg.intensity.overrideState = true; fg.intensity.value = 0.22f;
        fg.type.overrideState = true; fg.type.value = FilmGrainLookup.Thin1;

        EditorUtility.SetDirty(profile);
        Debug.Log("[ArtPass] Volume: ACES + contraste 18 + vinheta + grão (equivalente do AgX Medium High Contrast).");
    }

    private static T GetOrAdd<T>(VolumeProfile p) where T : VolumeComponent
    {
        if (p.TryGet(out T existing) && existing != null) return existing;

        // VolumeProfile.Add<T>() cria o componente APENAS em memória. Num script de
        // Editor ele é destruído no domain reload seguinte e deixa uma entrada nula
        // em profile.components — e aí qualquer leitura de Volume.profile (o
        // SuspicionVisualFeedback faz isso no Awake) estoura MissingReferenceException.
        // Para persistir, o componente precisa virar sub-asset do profile.
        T comp = ScriptableObject.CreateInstance<T>();
        comp.name = typeof(T).Name;
        comp.hideFlags = HideFlags.HideInHierarchy;
        p.components.Add(comp);
        AssetDatabase.AddObjectToAsset(comp, p);
        EditorUtility.SetDirty(p);
        return comp;
    }

    private static void DisableStrayDirectionalLight()
    {
        GameObject dl = GameObject.Find("Directional Light");
        if (dl == null) return;
        Light l = dl.GetComponent<Light>();
        if (l == null || l.type != LightType.Directional || !l.enabled) return;

        Undo.RecordObject(l, "Disable directional light");
        l.enabled = false;
        Debug.Log("[ArtPass] 'Directional Light' desativada — a sala não tem janelas; " +
                  "sol vindo de lugar nenhum achatava o contraste noir.");
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform c in root)
        {
            Transform r = FindDeep(c, name);
            if (r != null) return r;
        }
        return null;
    }
}
