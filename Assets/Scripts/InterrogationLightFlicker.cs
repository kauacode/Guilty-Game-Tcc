using System.Collections;
using UnityEngine;

/// <summary>
/// Simula uma lâmpada fluorescente velha e defeituosa.
/// Aguarda um intervalo aleatório, depois dispara um burst de
/// piscadas irregulares antes de retornar ao estado normal.
/// </summary>
[RequireComponent(typeof(Light))]
public class InterrogationLightFlicker : MonoBehaviour
{
    // ── Intervalo de espera entre falhas ─────────────────────────────────────
    [Header("Intervalo entre Falhas")]
    [SerializeField, Min(0f)] private float minIdleSeconds = 15f;
    [SerializeField, Min(0f)] private float maxIdleSeconds = 45f;

    // ── Configuração do burst de piscadas ────────────────────────────────────
    [Header("Burst de Piscadas")]
    [SerializeField, Min(0f)] private float minBurstDuration = 0.5f;
    [SerializeField, Min(0f)] private float maxBurstDuration = 1.0f;

    // Tempo mínimo e máximo que cada estado individual dura dentro do burst.
    // Valores baixos = piscada mais caótica e nervosa.
    [SerializeField, Min(0.01f)] private float minFlickerStep = 0.02f;
    [SerializeField, Min(0.01f)] private float maxFlickerStep = 0.10f;

    // ── Intensidades ─────────────────────────────────────────────────────────
    [Header("Intensidade da Luz")]
    [SerializeField, Min(0f)] private float normalIntensity  = 1.5f;

    // Durante o burst a luz alterna entre estes valores de forma aleatória,
    // simulando um mau contato: apagada, muito fraca, parcial e normal.
    [SerializeField, Min(0f)] private float dimIntensity     = 0.08f;
    [SerializeField, Min(0f)] private float partialIntensity = 0.55f;

    // ── Áudio (opcional) ─────────────────────────────────────────────────────
    [Header("Áudio (opcional)")]
    [SerializeField] private AudioSource flickerAudioSource;

    // ─────────────────────────────────────────────────────────────────────────

    private Light       _light;
    private Coroutine   _flickerRoutine;

    // Pesos para sortear o próximo estado durante o burst.
    // A luz apagada (0) aparece com mais frequência para aumentar tensão.
    private static readonly (float intensity, int weight)[] FlickerStates =
    {
        (0f,   5),   // apagada  — peso 5
        (0f,   3),   // apagada  — reforço
        (-1f,  1),   // dim      — resolvido em runtime via dimIntensity
        (-2f,  1),   // partial  — resolvido em runtime via partialIntensity
        (-3f,  1),   // normal   — resolvido em runtime via normalIntensity
    };

    private static readonly int TotalWeight = CalculateTotalWeight();

    private static int CalculateTotalWeight()
    {
        int total = 0;
        foreach (var state in FlickerStates) total += state.weight;
        return total;
    }

    // ── Ciclo de vida ─────────────────────────────────────────────────────────

    private void Awake()
    {
        _light = GetComponent<Light>();
        _light.intensity = normalIntensity;
    }

    private void OnEnable()
    {
        _flickerRoutine = StartCoroutine(FlickerLoop());
    }

    private void OnDisable()
    {
        if (_flickerRoutine != null)
            StopCoroutine(_flickerRoutine);

        // Garante que a luz volte ao normal se o objeto for desativado
        // durante um burst (ex: cutscene, pausa).
        if (_light != null)
            _light.intensity = normalIntensity;
    }

    // ── Coroutines ────────────────────────────────────────────────────────────

    private IEnumerator FlickerLoop()
    {
        while (true)
        {
            // Aguarda um tempo aleatório antes da próxima falha.
            float idleTime = Random.Range(minIdleSeconds, maxIdleSeconds);
            yield return new WaitForSeconds(idleTime);

            yield return StartCoroutine(ExecuteFlickerBurst());
        }
    }

    private IEnumerator ExecuteFlickerBurst()
    {
        PlayFlickerAudio();

        float burstDuration  = Random.Range(minBurstDuration, maxBurstDuration);
        float burstElapsed   = 0f;

        while (burstElapsed < burstDuration)
        {
            _light.intensity = SampleFlickerIntensity();

            float stepDuration = Random.Range(minFlickerStep, maxFlickerStep);
            yield return new WaitForSeconds(stepDuration);
            burstElapsed += stepDuration;
        }

        // Restaura intensidade normal ao fim do burst.
        _light.intensity = normalIntensity;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Sorteia um valor de intensidade com base nos pesos definidos em FlickerStates.
    /// Valores negativos são mapeados para dimIntensity, partialIntensity e normalIntensity
    /// em tempo de execução, permitindo ajuste pelo Inspector sem recompilar.
    /// </summary>
    private float SampleFlickerIntensity()
    {
        int roll = Random.Range(0, TotalWeight);
        int accumulated = 0;

        foreach (var (intensity, weight) in FlickerStates)
        {
            accumulated += weight;
            if (roll < accumulated)
            {
                return intensity switch
                {
                    -1f => dimIntensity,
                    -2f => partialIntensity,
                    -3f => normalIntensity,
                    _   => intensity,   // 0f (apagada)
                };
            }
        }

        return 0f;
    }

    private void PlayFlickerAudio()
    {
        if (flickerAudioSource != null && !flickerAudioSource.isPlaying)
            flickerAudioSource.Play();
    }
}
