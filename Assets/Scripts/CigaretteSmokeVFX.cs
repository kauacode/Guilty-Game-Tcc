using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(ParticleSystem))]
public class CigaretteSmokeVFX : MonoBehaviour
{
    [Header("Emissão")]
    [SerializeField, Min(0f)] private float emissionRate = 4f;
    [SerializeField, Min(1)]  private int   maxParticles = 50;

    [Header("Vida útil das partículas")]
    [SerializeField, Min(0f)] private float lifetimeMin = 3f;
    [SerializeField, Min(0f)] private float lifetimeMax = 5f;

    [Header("Velocidade inicial")]
    [SerializeField, Min(0f)] private float startSpeedMin = 0.15f;
    [SerializeField, Min(0f)] private float startSpeedMax = 0.35f;

    [Header("Tamanho (volumoso)")]
    [SerializeField, Min(0f)] private float startSize = 0.07f;
    [SerializeField, Min(0f)] private float endSize   = 0.35f;

    [Header("Cor")]
    [SerializeField] private Color smokeColor = new Color(0.5f, 0.5f, 0.5f, 1f);

    [Header("Opacidade")]
    [SerializeField, Range(0f, 1f)] private float peakAlpha = 0.35f;

    [Header("Ruído orgânico (ar quente)")]
    [SerializeField, Min(0f)] private float noiseStrength  = 0.08f;
    [SerializeField, Min(0f)] private float noiseFrequency = 0.2f;

    [Header("Rotação")]
    [SerializeField, Min(0f)] private float rotationSpeedMaxDegrees = 20f;

    private void Awake()
    {
        SetupParticleSystem();
        SetupRenderer();
    }

    private void SetupParticleSystem()
    {
        var ps = GetComponent<ParticleSystem>();

        ConfigureMain(ps);
        ConfigureEmission(ps);
        ConfigureShape(ps);
        ConfigureSizeOverLifetime(ps);
        ConfigureColorOverLifetime(ps);
        ConfigureRotationOverLifetime(ps);
        ConfigureNoise(ps);
    }

    private void ConfigureMain(ParticleSystem ps)
    {
        var main = ps.main;
        main.loop            = true;
        main.playOnAwake     = true;
        main.maxParticles    = maxParticles;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(lifetimeMin, lifetimeMax);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(startSpeedMin, startSpeedMax);
        main.startSize       = startSize;
        // Alfa PRECISA ser 1 aqui: a cor final = startColor x colorOverLifetime.
        // Se startColor.a fosse 0, o produto seria sempre 0 e a fumaça ficaria
        // invisível mesmo com a curva de fade em colorOverLifetime correta.
        main.startColor      = new ParticleSystem.MinMaxGradient(smokeColor);
        main.gravityModifier = -0.015f;
        // Rotação inicial aleatória (0-360°, expressa em radianos: -π a π)
        main.startRotation3D = false;
        main.startRotation   = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
    }

    private void ConfigureEmission(ParticleSystem ps)
    {
        var emission = ps.emission;
        emission.enabled      = true;
        emission.rateOverTime = emissionRate;
    }

    private void ConfigureShape(ParticleSystem ps)
    {
        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle     = 3f;
        shape.radius    = 0.001f;
        // Cone emite em +Z local por padrão; -90° X aponta para +Y (cima)
        shape.rotation  = new Vector3(-90f, 0f, 0f);
    }

    private void ConfigureSizeOverLifetime(ParticleSystem ps)
    {
        var sizeOL = ps.sizeOverLifetime;
        sizeOL.enabled = true;

        // Fumaça nasce pequena e incha até formar um "puff" volumoso antes de dissipar
        var curve = new AnimationCurve(
            new Keyframe(0f,   0f,    0f, 2.5f),
            new Keyframe(0.4f, 0.55f, 0f, 0f),
            new Keyframe(1f,   1f,    0f, 0f)
        );
        // MinMaxCurve(multiplier, normalizedCurve): tamanho real = curve(t) * endSize
        sizeOL.size = new ParticleSystem.MinMaxCurve(endSize, curve);
    }

    private void ConfigureColorOverLifetime(ParticleSystem ps)
    {
        var colorOL = ps.colorOverLifetime;
        colorOL.enabled = true;
        colorOL.color   = new ParticleSystem.MinMaxGradient(BuildSmokeGradient());
    }

    private void ConfigureRotationOverLifetime(ParticleSystem ps)
    {
        var rotationOL = ps.rotationOverLifetime;
        rotationOL.enabled       = true;
        rotationOL.separateAxes  = false;
        // Graus/segundo — cada puff gira suavemente, sentido sorteado por partícula
        rotationOL.z = new ParticleSystem.MinMaxCurve(-rotationSpeedMaxDegrees, rotationSpeedMaxDegrees);
    }

    private void ConfigureNoise(ParticleSystem ps)
    {
        var noise = ps.noise;
        noise.enabled     = true;
        noise.strength    = noiseStrength;
        noise.frequency   = noiseFrequency;
        noise.scrollSpeed = 0.15f;
        noise.quality     = ParticleSystemNoiseQuality.Medium;
        noise.damping     = true;
    }

    private void SetupRenderer()
    {
        var psr = GetComponent<ParticleSystemRenderer>();
        psr.renderMode         = ParticleSystemRenderMode.Billboard;
        psr.sortMode           = ParticleSystemSortMode.YoungestInFront;
        psr.shadowCastingMode  = ShadowCastingMode.Off;
        psr.receiveShadows     = false;

        psr.sharedMaterial = BuildSmokeMaterial();
    }

    /// <summary>
    /// Cria um material próprio com uma textura de névoa radial gerada em
    /// runtime. Usa o shader Sprites/Default: alfa-blend já vem configurado
    /// de fábrica e é compatível tanto com URP quanto Built-in sem precisar
    /// mexer manualmente em propriedades de blend (fonte dos bugs anteriores).
    /// </summary>
    private Material BuildSmokeMaterial()
    {
        Shader shader = Shader.Find("Sprites/Default");
        var mat = new Material(shader)
        {
            mainTexture = GenerateSoftParticleTexture(),
            color       = Color.white, // a cor real vem do vértice (colorOverLifetime)
        };
        return mat;
    }

    /// <summary>
    /// Gera uma textura 64x64 com gradiente radial (branco opaco no centro,
    /// alfa 0 na borda) para que cada partícula pareça um floco de névoa
    /// suave em vez de um quadrado/ponto sólido.
    /// </summary>
    private Texture2D GenerateSoftParticleTexture()
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        var pixels = new Color[size * size];
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float maxDist = size / 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center) / maxDist;
                float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(dist));
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private Gradient BuildSmokeGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            colorKeys: new[]
            {
                new GradientColorKey(smokeColor, 0f),
                new GradientColorKey(smokeColor, 1f),
            },
            alphaKeys: new[]
            {
                new GradientAlphaKey(0f,         0f),
                new GradientAlphaKey(peakAlpha,  0.25f),
                new GradientAlphaKey(peakAlpha,  0.70f),
                new GradientAlphaKey(0f,         1f),
            }
        );
        return g;
    }
}
