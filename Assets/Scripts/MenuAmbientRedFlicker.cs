using System.Collections;
using UnityEngine;

/// <summary>
/// Sirene policial no menu: alterna vermelho e azul a cada flash,
/// simulando a luz de viatura. Entre os bursts mantém o vermelho base.
/// </summary>
[RequireComponent(typeof(Light))]
public class MenuAmbientRedFlicker : MonoBehaviour
{
    [Header("Intensidade base (entre pulsos)")]
    [SerializeField, Min(0f)] private float baseIntensity = 1.4f;

    [Header("Intervalo entre bursts")]
    [SerializeField, Min(0f)] private float minIdleSeconds = 2f;
    [SerializeField, Min(0f)] private float maxIdleSeconds = 7f;

    [Header("Burst de sirene")]
    [SerializeField, Min(0f)] private float peakIntensity = 9.0f;
    [SerializeField, Min(1)]  private int   minFlashes    = 3;
    [SerializeField, Min(1)]  private int   maxFlashes    = 9;
    [SerializeField, Min(0.01f)] private float flashOnTime  = 0.09f;
    [SerializeField, Min(0.01f)] private float flashOffTime = 0.05f;
    [SerializeField, Min(0f)]    private float groupGap     = 0.14f;

    [Header("Cores da sirene")]
    [SerializeField] private Color sirenRed  = new Color(1f,    0.04f, 0.04f);
    [SerializeField] private Color sirenBlue = new Color(0.12f, 0.38f, 1f);

    private Light _light;

    private void Awake()
    {
        _light = GetComponent<Light>();
        _light.color     = sirenRed;
        _light.intensity = baseIntensity;
    }

    private void OnEnable()  => StartCoroutine(PulseLoop());
    private void OnDisable() { StopAllCoroutines(); if (_light) { _light.color = sirenRed; _light.intensity = baseIntensity; } }

    private IEnumerator PulseLoop()
    {
        yield return new WaitForSeconds(Random.Range(1f, 4f));

        while (true)
        {
            yield return new WaitForSeconds(Random.Range(minIdleSeconds, maxIdleSeconds));
            yield return StartCoroutine(DoPulseGroup());
        }
    }

    private IEnumerator DoPulseGroup()
    {
        int flashes = Random.Range(minFlashes, maxFlashes + 1);

        for (int i = 0; i < flashes; i++)
        {
            // Alterna vermelho/azul a cada flash — efeito de sirene policial
            _light.color     = (i % 2 == 0) ? sirenRed : sirenBlue;
            _light.intensity = peakIntensity;
            yield return new WaitForSeconds(flashOnTime);
            _light.intensity = 0f;
            yield return new WaitForSeconds(flashOffTime);
        }

        // Volta ao vermelho base entre os bursts
        yield return new WaitForSeconds(groupGap);
        _light.color     = sirenRed;
        _light.intensity = baseIntensity;
    }
}
