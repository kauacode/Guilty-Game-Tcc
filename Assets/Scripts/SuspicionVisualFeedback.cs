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

    [Header("Base noir")]
    [Tooltip("Vinheta de repouso, com suspeita zero. O feedback de suspeita soma por cima " +
             "disto em vez de partir do preto. Sem esta base o script zerava a vinheta no " +
             "Awake e o filtro noir ficava sem vinheta nenhuma.")]
    [SerializeField] private float baseIntensity = 0.28f;
    [SerializeField] private Color baseColor = new Color(0.03f, 0.04f, 0.07f, 1f);

    [Header("Curva de intensidade")]
    [SerializeField] private float maxVignetteIntensity = 0.45f;
    [SerializeField] private float lieDetectedIntensity = 0.75f;
    [SerializeField] private float fadeSpeed = 2f;

    private Vignette vignette;
    private float targetIntensity;
    private Color targetColor;
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

        // parte da base noir, não do zero
        targetIntensity = baseIntensity;
        targetColor = baseColor;
        vignette.intensity.value = baseIntensity;
        vignette.color.value = baseColor;
    }

    // A inscrição fica no Start, NÃO no OnEnable.
    //
    // A Unity roda Awake -> OnEnable por objeto, em sequência: o OnEnable deste
    // script podia rodar ANTES do Awake do ApiClient. Aí Instance era null, o if
    // pulava a inscrição em silêncio e o feedback de suspeita nunca funcionava —
    // sem erro, sem log. Todo Awake termina antes de qualquer Start, então aqui a
    // referência já existe. É o mesmo padrão que o UIController já usava.
    private bool subscribed;
    private bool started;

    private void Start()
    {
        started = true;
        Subscribe();
    }

    private void OnEnable()
    {
        // reativar o objeto depois do Start precisa reinscrever
        if (started) Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (subscribed) return;

        if (ApiClient.Instance != null)
            ApiClient.Instance.OnResponseReceived += HandleApiResponse;
        else
            Debug.LogWarning("[SuspicionVisualFeedback] ApiClient.Instance ausente — " +
                             "o feedback de suspeita não vai reagir às respostas da API.");

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnLieDetected += HandleLieDetected;
            GameManager.Instance.OnGameOver += HandleGameOver;
            GameManager.Instance.OnSuspicionChanged += HandleSuspicionChanged;
        }
        else
        {
            Debug.LogWarning("[SuspicionVisualFeedback] GameManager.Instance ausente — " +
                             "mentira detectada e fim de jogo não vão acender a vinheta.");
        }

        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed) return;

        if (ApiClient.Instance != null)
            ApiClient.Instance.OnResponseReceived -= HandleApiResponse;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnLieDetected -= HandleLieDetected;
            GameManager.Instance.OnGameOver -= HandleGameOver;
            GameManager.Instance.OnSuspicionChanged -= HandleSuspicionChanged;
        }

        subscribed = false;
    }

    /// <summary>Volta ao repouso noir quando o GameManager reseta a suspeita.</summary>
    private void HandleSuspicionChanged(int suspicion)
    {
        if (suspicion != 0) return;
        if (liePulseRoutine != null)
        {
            StopCoroutine(liePulseRoutine);
            liePulseRoutine = null;
        }
        targetIntensity = baseIntensity;
        targetColor = baseColor;
    }

    private void HandleApiResponse(AnalyzeResponse response)
    {
        int suspicion = response.status_investigacao.nivel_suspeita;
        float t = Mathf.Clamp01(suspicion / 100f);

        // soma por cima da base noir em vez de substituí-la
        targetIntensity = baseIntensity + t * maxVignetteIntensity;

        if (ColorUtility.TryParseHtmlString(response.feedback_visual.cor_iluminacao, out Color parsedColor))
        {
            // com suspeita baixa o tom noir domina; a cor da API entra conforme a tensão sobe
            targetColor = Color.Lerp(baseColor, parsedColor, t);
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
