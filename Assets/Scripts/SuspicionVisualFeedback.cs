using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Move o feedback visual de suspeita (antes pintado direto no Canvas 2D)
/// para um override de Vignette no Global Volume da cena, para o efeito
/// continuar visível mesmo com o painel de interrogatório fechado.
/// Assina os eventos já existentes de ApiClient/GameManager — não altera
/// nada na lógica interna desses dois scripts.
/// </summary>
[RequireComponent(typeof(Volume))]
public class SuspicionVisualFeedback : MonoBehaviour
{
    [Header("Referência")]
    [SerializeField] private Volume globalVolume;

    [Header("Curva de intensidade")]
    [SerializeField] private float maxVignetteIntensity = 0.45f;
    [SerializeField] private float lieDetectedIntensity = 0.75f;
    [SerializeField] private float fadeSpeed = 2f;

    private Vignette vignette;
    private float targetIntensity;
    private Color targetColor = Color.black;
    private Coroutine liePulseRoutine;

    private void Awake()
    {
        if (globalVolume == null)
        {
            globalVolume = GetComponent<Volume>();
        }

        if (!globalVolume.profile.TryGet(out vignette))
        {
            vignette = globalVolume.profile.Add<Vignette>(true);
        }

        vignette.intensity.overrideState = true;
        vignette.color.overrideState = true;
        vignette.intensity.value = 0f;
    }

    private void OnEnable()
    {
        if (ApiClient.Instance != null)
        {
            ApiClient.Instance.OnResponseReceived += HandleApiResponse;
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnLieDetected += HandleLieDetected;
            GameManager.Instance.OnGameOver += HandleGameOver;
        }
    }

    private void OnDisable()
    {
        if (ApiClient.Instance != null)
        {
            ApiClient.Instance.OnResponseReceived -= HandleApiResponse;
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnLieDetected -= HandleLieDetected;
            GameManager.Instance.OnGameOver -= HandleGameOver;
        }
    }

    private void HandleApiResponse(AnalyzeResponse response)
    {
        int suspicion = response.status_investigacao.nivel_suspeita;
        targetIntensity = Mathf.Clamp01(suspicion / 100f) * maxVignetteIntensity;

        if (ColorUtility.TryParseHtmlString(response.feedback_visual.cor_iluminacao, out Color parsedColor))
        {
            targetColor = parsedColor;
        }
    }

    private void HandleLieDetected(bool detected)
    {
        if (!detected)
        {
            return;
        }

        if (liePulseRoutine != null)
        {
            StopCoroutine(liePulseRoutine);
        }

        liePulseRoutine = StartCoroutine(LiePulse());
    }

    private void HandleGameOver()
    {
        targetIntensity = 1f;
        targetColor = Color.red;
    }

    private IEnumerator LiePulse()
    {
        float previousTarget = targetIntensity;
        targetIntensity = Mathf.Max(previousTarget, lieDetectedIntensity);
        yield return new WaitForSeconds(0.4f);
        targetIntensity = previousTarget;
        liePulseRoutine = null;
    }

    private void Update()
    {
        vignette.intensity.value = Mathf.MoveTowards(vignette.intensity.value, targetIntensity, fadeSpeed * Time.deltaTime);
        vignette.color.value = Color.Lerp(vignette.color.value, targetColor, Time.deltaTime * fadeSpeed);
    }
}
