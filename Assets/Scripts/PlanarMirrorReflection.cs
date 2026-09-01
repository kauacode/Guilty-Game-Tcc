using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Reflexo planar para o espelho falso da sala de interrogatório.
///
/// Por que existe: o URP não tem screen space reflections, e Reflection Probe é um
/// cubemap capturado de um ponto único — serve para superfície curva ou rugosa, não
/// para espelho plano. Sem isto o espelho mostra só um gradiente com os pontos de luz
/// "no infinito", que era o sintoma.
///
/// Como funciona: a cada frame espelha a câmera principal através do plano do vidro,
/// renderiza numa RenderTexture e entrega para o shader Guilty/TwoWayMirror, que a
/// amostra em espaço de tela.
///
/// O plano é derivado da própria malha (o eixo local mais fino do bounds), então
/// funciona sem configuração manual mesmo que o espelho seja movido ou girado.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Renderer))]
public class PlanarMirrorReflection : MonoBehaviour
{
    [Header("Qualidade")]
    [Tooltip("Lado da RenderTexture. 512 basta: o vidro é pequeno em tela e o reflexo é escurecido.")]
    [SerializeField] private int textureSize = 512;

    [Tooltip("Camadas que aparecem no reflexo.")]
    [SerializeField] private LayerMask reflectLayers = ~0;

    [Tooltip("Empurra o plano de corte para trás do vidro, evitando costura na borda.")]
    [SerializeField] private float clipPlaneOffset = 0.015f;

    [Header("Custo")]
    [Tooltip("Não renderiza o reflexo se o espelho não estiver visível para a câmera.")]
    [SerializeField] private bool skipWhenNotVisible = true;

    private Camera reflectionCamera;
    private RenderTexture reflectionTexture;
    private Renderer mirrorRenderer;
    private MaterialPropertyBlock props;

    // Guarda contra recursão: a câmera de reflexão dispara beginCameraRendering de novo.
    private static bool rendering;

    private static readonly int ReflectionTexId = Shader.PropertyToID("_ReflectionTex");

    private void OnEnable()
    {
        mirrorRenderer = GetComponent<Renderer>();
        props = new MaterialPropertyBlock();
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        Cleanup();
    }

    private void Cleanup()
    {
        if (reflectionCamera != null)
        {
            if (Application.isPlaying) Destroy(reflectionCamera.gameObject);
            else DestroyImmediate(reflectionCamera.gameObject);
            reflectionCamera = null;
        }
        if (reflectionTexture != null)
        {
            if (Application.isPlaying) Destroy(reflectionTexture);
            else DestroyImmediate(reflectionTexture);
            reflectionTexture = null;
        }
    }

    private void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
    {
        if (rendering || mirrorRenderer == null) return;
        if (cam == reflectionCamera) return;
        if (cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;
        if (skipWhenNotVisible && !mirrorRenderer.isVisible) return;

        // ---- plano do espelho, derivado da malha ----
        Vector3 normal = GetMirrorNormal(cam);
        Vector3 pos    = transform.position;

        // Nada a refletir se a câmera está atrás do vidro.
        if (Vector3.Dot(cam.transform.position - pos, normal) < 0f) return;

        EnsureResources(cam);

        // ---- espelha a câmera através do plano ----
        float d = -Vector3.Dot(normal, pos) - clipPlaneOffset;
        Matrix4x4 reflection = CalculateReflectionMatrix(new Vector4(normal.x, normal.y, normal.z, d));

        reflectionCamera.CopyFrom(cam);
        reflectionCamera.targetTexture      = reflectionTexture;
        reflectionCamera.cullingMask        = reflectLayers;
        reflectionCamera.enabled            = false;

        // O Transform precisa ir junto com a matriz. Camera.CopyFrom copia
        // configurações, não posição — e culling, ordenação de transparentes e
        // seleção de luzes adicionais usam o Transform. Deixá-lo na origem
        // enquanto a matriz aponta para outro lugar faz o reflexo perder luzes.
        Vector3 reflectedPos = reflection.MultiplyPoint(cam.transform.position);
        Vector3 fwd = Vector3.Reflect(cam.transform.forward, normal);
        Vector3 up  = Vector3.Reflect(cam.transform.up, normal);
        reflectionCamera.transform.SetPositionAndRotation(reflectedPos, Quaternion.LookRotation(fwd, up));

        reflectionCamera.worldToCameraMatrix = cam.worldToCameraMatrix * reflection;

        // Near plane oblíquo: descarta tudo que está atrás do vidro, senão a parede
        // de trás e o miolo do bloco apareceriam no reflexo.
        Vector4 clipPlane = CameraSpacePlane(reflectionCamera, pos, normal, 1.0f);
        reflectionCamera.projectionMatrix = reflectionCamera.CalculateObliqueMatrix(clipPlane);

        var extra = reflectionCamera.GetUniversalAdditionalCameraData();
        if (extra != null)
        {
            extra.renderShadows        = false;  // sombras no reflexo não pagam o custo
            extra.requiresDepthTexture = false;
            extra.requiresColorTexture = false;
            extra.renderPostProcessing = false;  // o post do frame principal já cobre
        }

        // A matriz de reflexão inverte a orientação dos triângulos.
        rendering = true;
        GL.invertCulling = true;
        RenderReflection();
        GL.invertCulling = false;
        rendering = false;

        props.Clear();
        mirrorRenderer.GetPropertyBlock(props);
        props.SetTexture(ReflectionTexId, reflectionTexture);
        mirrorRenderer.SetPropertyBlock(props);
    }

    private void RenderReflection()
    {
#if UNITY_2023_3_OR_NEWER
        var request = new UniversalRenderPipeline.SingleCameraRequest();
        if (RenderPipeline.SupportsRenderRequest(reflectionCamera, request))
        {
            request.destination = reflectionTexture;
            RenderPipeline.SubmitRenderRequest(reflectionCamera, request);
            return;
        }
#endif
        reflectionCamera.Render();
    }

    private void EnsureResources(Camera cam)
    {
        if (reflectionTexture == null || reflectionTexture.width != textureSize)
        {
            if (reflectionTexture != null) DestroyImmediate(reflectionTexture);
            reflectionTexture = new RenderTexture(textureSize, textureSize, 24,
                                                  RenderTextureFormat.DefaultHDR)
            {
                name = "MirrorReflection",
                antiAliasing = 1,
                useMipMap = false,
                hideFlags = HideFlags.HideAndDontSave,
            };
            reflectionTexture.Create();
        }

        if (reflectionCamera == null)
        {
            var go = new GameObject("MirrorReflectionCamera", typeof(Camera));
            go.hideFlags = HideFlags.HideAndDontSave;
            reflectionCamera = go.GetComponent<Camera>();
            reflectionCamera.enabled = false;
        }
    }

    /// <summary>
    /// O espelho é uma caixa achatada. O eixo local de menor extensão é a normal;
    /// o sinal é escolhido para apontar na direção da câmera.
    /// </summary>
    private Vector3 GetMirrorNormal(Camera cam)
    {
        Vector3 size = mirrorRenderer.localBounds.size;
        Vector3 localNormal =
            (size.x <= size.y && size.x <= size.z) ? Vector3.right :
            (size.y <= size.z)                     ? Vector3.up    : Vector3.forward;

        Vector3 n = transform.TransformDirection(localNormal).normalized;
        if (Vector3.Dot(cam.transform.position - transform.position, n) < 0f) n = -n;
        return n;
    }

    private static Matrix4x4 CalculateReflectionMatrix(Vector4 p)
    {
        Matrix4x4 m = Matrix4x4.identity;
        m.m00 = 1f - 2f * p.x * p.x; m.m01 = -2f * p.x * p.y;      m.m02 = -2f * p.x * p.z;      m.m03 = -2f * p.x * p.w;
        m.m10 = -2f * p.y * p.x;     m.m11 = 1f - 2f * p.y * p.y;  m.m12 = -2f * p.y * p.z;      m.m13 = -2f * p.y * p.w;
        m.m20 = -2f * p.z * p.x;     m.m21 = -2f * p.z * p.y;      m.m22 = 1f - 2f * p.z * p.z;  m.m23 = -2f * p.z * p.w;
        return m;
    }

    private Vector4 CameraSpacePlane(Camera cam, Vector3 pos, Vector3 normal, float sideSign)
    {
        Vector3 offsetPos = pos + normal * clipPlaneOffset;
        Matrix4x4 m = cam.worldToCameraMatrix;
        Vector3 cpos = m.MultiplyPoint(offsetPos);
        Vector3 cnormal = m.MultiplyVector(normal).normalized * sideSign;
        return new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal));
    }
}
