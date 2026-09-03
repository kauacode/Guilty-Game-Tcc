using System.Collections;
using UnityEngine;
using TMPro;

/// <summary>
/// Representa um dossiê no quadro de investigação.
///
/// Ao ganhar foco:
///   - avança 4 cm em direção à câmera
///   - pulsa escala brevemente (feedback tátil)
///   - aplica emissão dourada sutil ao renderer do papel
///   - exibe label flutuante com o id do caso (fade in)
///
/// Todos os filhos acompanham automaticamente o pai.
/// </summary>
[RequireComponent(typeof(Collider))]
public class CaseItem : MonoBehaviour
{
    [Header("Dados do Caso")]
    [SerializeField] public CaseInfo caseData = new CaseInfo();

    // Propriedades de conveniência para retrocompatibilidade
    public string targetSceneName => caseData?.targetSceneName ?? "";
    public bool   isLocked        => caseData?.isLocked ?? false;
    public string caseId          => caseData?.caseId ?? "";

    [Header("Animação de foco")]
    [SerializeField] private float forwardOffset = 0.045f;
    [SerializeField] private float animSpeed     = 7f;

    [Header("Glow sutil")]
    [SerializeField] private Color glowColor     = new Color(1.0f, 0.75f, 0.25f);
    [SerializeField] private float glowIntensity = 0.30f;

    [Header("Pulse de escala")]
    [SerializeField] private float pulseScale = 1.018f;
    [SerializeField] private float pulseSpeed = 9f;

    [Header("Label flutuante")]
    [SerializeField] private float labelYOffset     = 0.15f;
    [SerializeField] private float labelZOffset      = -0.02f;
    [SerializeField] private float labelFontSize     = 3f;
    [SerializeField] private float labelFadeSpeed    = 6f;
    [SerializeField] private Color labelColor        = new Color(1f, 0.95f, 0.80f);

    // ── estado ─────────────────────────────────────────────────────────────
    private Vector3 restWorldPos;
    private Vector3 targetWorldPos;
    private Vector3 baseScale;
    private float   currentScale = 1f;
    private float   targetScale  = 1f;
    private bool    focused;

    private Renderer selfRend;
    private Material instanceMat;

    private TextMeshPro caseLabel;
    private float       labelAlpha;
    private float       labelTargetAlpha;

    // ── ciclo de vida ───────────────────────────────────────────────────────

    private void Awake()
    {
        restWorldPos   = transform.position;
        targetWorldPos = restWorldPos;
        baseScale      = transform.localScale;

        selfRend = GetComponent<Renderer>();
        if (selfRend == null)
            selfRend = GetComponentInChildren<Renderer>(false);

        if (selfRend != null)
        {
            instanceMat = selfRend.material;
            instanceMat.EnableKeyword("_EMISSION");
            instanceMat.SetColor("_EmissionColor", Color.black);
        }

        CreateLabel();
    }

    private void Start()
    {
        // Orienta o label para a câmera (câmera fixa nesta cena)
        if (caseLabel != null && Camera.main != null)
            OrientLabelToCamera();
    }

    private void Update()
    {
        // posição
        if (Vector3.SqrMagnitude(transform.position - targetWorldPos) > 0.000001f)
            transform.position = Vector3.Lerp(transform.position, targetWorldPos, Time.deltaTime * animSpeed);

        // escala pulse
        if (Mathf.Abs(currentScale - targetScale) > 0.0001f)
        {
            currentScale = Mathf.Lerp(currentScale, targetScale, Time.deltaTime * pulseSpeed);
            transform.localScale = baseScale * currentScale;
        }

        // label fade
        if (Mathf.Abs(labelAlpha - labelTargetAlpha) > 0.001f)
        {
            labelAlpha = Mathf.Lerp(labelAlpha, labelTargetAlpha, Time.deltaTime * labelFadeSpeed);
            if (caseLabel != null)
                caseLabel.color = new Color(labelColor.r, labelColor.g, labelColor.b, labelAlpha);
        }
    }

    // ── API pública ─────────────────────────────────────────────────────────

    public void SetFocused(bool value)
    {
        if (focused == value) return;
        focused = value;

        if (value)
        {
            Camera cam = Camera.main;
            Vector3 dir = cam != null
                ? (cam.transform.position - restWorldPos).normalized
                : Vector3.back;

            targetWorldPos   = restWorldPos + dir * forwardOffset;
            targetScale      = pulseScale;
            labelTargetAlpha = 1f;
            StartCoroutine(PulseBack());

            if (instanceMat != null)
                instanceMat.SetColor("_EmissionColor", glowColor * glowIntensity);
        }
        else
        {
            targetWorldPos   = restWorldPos;
            targetScale      = 1f;
            labelTargetAlpha = 0f;

            if (instanceMat != null)
                instanceMat.SetColor("_EmissionColor", Color.black);
        }
    }

    public bool IsFocused => focused;

    // ── helpers ─────────────────────────────────────────────────────────────

    private void CreateLabel()
    {
        var go = new GameObject("CaseLabel");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, labelYOffset, labelZOffset);

        caseLabel           = go.AddComponent<TextMeshPro>();
        caseLabel.text      = caseData?.caseId ?? "CASO";
        caseLabel.fontSize  = labelFontSize;
        caseLabel.alignment = TextAlignmentOptions.Center;
        caseLabel.color     = new Color(labelColor.r, labelColor.g, labelColor.b, 0f);
        caseLabel.enableWordWrapping = false;

        labelAlpha       = 0f;
        labelTargetAlpha = 0f;
    }

    private void OrientLabelToCamera()
    {
        if (caseLabel == null || Camera.main == null) return;
        Vector3 toCamera = Camera.main.transform.position - caseLabel.transform.position;
        caseLabel.transform.rotation = Quaternion.LookRotation(toCamera, Vector3.up);
    }

    private IEnumerator PulseBack()
    {
        yield return new WaitForSeconds(0.15f);
        targetScale = 1f;
    }

    private void OnDestroy()
    {
        if (instanceMat != null) Destroy(instanceMat);
    }
}
