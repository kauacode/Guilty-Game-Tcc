using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(ParticleSystem))]
public class CigaretteSmokeVFX : MonoBehaviour
{
    [Header("Emissão")]
    [SerializeField, Min(0f)] private float emissionRate = 3f;
    [SerializeField, Min(1)]  private int   maxParticles = 50;

    [Header("Vida útil das partículas")]
    [SerializeField, Min(0f)] private float lifetimeMin = 3f;
    [SerializeField, Min(0f)] private float lifetimeMax = 5f;

    [Header("Velocidade inicial")]
    [SerializeField, Min(0f)] private float startSpeedMin = 0.15f;
    [SerializeField, Min(0f)] private float startSpeedMax = 0.35f;

    [Header("Tamanho")]
    [SerializeField, Min(0f)] private float startSize = 0.015f;
    [SerializeField, Min(0f)] private float endSize   = 0.22f;

    [Header("Opacidade")]
    [SerializeField, Range(0f, 1f)] private float peakAlpha = 0.15f;

    [Header("Ruído orgânico")]
    [SerializeField, Min(0f)] private float noiseStrength  = 0.04f;
    [SerializeField, Min(0f)] private float noiseFrequency = 0.3f;

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
        main.startColor      = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.85f, 0.85f, 0f));
        main.gravityModifier = -0.015f;
        // Rotação aleatória em Z para evitar partículas alinhadas
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

        // Fumaça nasce minúscula e expande progressivamente enquanto sobe
        var curve = new AnimationCurve(
            new Keyframe(0f,   0f,   0f, 2.5f),
            new Keyframe(0.4f, 0.45f, 0f, 0f),
            new Keyframe(1f,   1f,   0f, 0f)
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

    private void ConfigureNoise(ParticleSystem ps)
    {
        var noise = ps.noise;
        noise.enabled     = true;
        noise.strength    = noiseStrength;
        noise.frequency   = noiseFrequency;
        noise.scrollSpeed = 0.15f;
        noise.quality     = ParticleSystemNoiseQuality.Low;
    }

    private void SetupRenderer()
    {
        var psr = GetComponent<ParticleSystemRenderer>();
        psr.renderMode         = ParticleSystemRenderMode.Billboard;
        psr.sortMode           = ParticleSystemSortMode.YoungestInFront;
        psr.shadowCastingMode  = ShadowCastingMode.Off;
        psr.receiveShadows     = false;

        var mat = ResolveMaterial();
        if (mat != null)
            psr.sharedMaterial = mat;
    }

    private Material ResolveMaterial()
    {
        // 1ª tentativa: material embutido no engine (funciona em todos pipelines)
        var mat = Resources.GetBuiltinResource<Material>("Default-Particle.mat");
        if (mat != null) return mat;

        // 2ª tentativa: shader URP
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader != null) return new Material(shader);

        // 3ª tentativa: shader legacy
        shader = Shader.Find("Particles/Standard Unlit");
        if (shader != null) return new Material(shader);

        return null;
    }

    private Gradient BuildSmokeGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            colorKeys: new[]
            {
                new GradientColorKey(new Color(0.82f, 0.82f, 0.82f), 0f),
                new GradientColorKey(new Color(0.88f, 0.88f, 0.88f), 0.5f),
                new GradientColorKey(new Color(0.94f, 0.94f, 0.94f), 1f),
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
