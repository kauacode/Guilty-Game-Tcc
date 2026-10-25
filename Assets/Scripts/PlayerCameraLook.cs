using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Mouselook de primeira pessoa com SmoothDamp para sensação de peso e inércia.
///
/// Arquitetura de dois acumuladores:
///   _targetPitch / _targetYaw  — recebem o delta cru do mouse e são clampados
///   _currentPitch / _currentYaw — perseguem o alvo via SmoothDamp (com inércia)
///
/// O clamp é aplicado apenas no alvo, garantindo que os limites sejam respeitados
/// mesmo com o trail suave — a câmera desacelera antes da borda, não depois.
/// </summary>
[RequireComponent(typeof(Transform))]
public class PlayerCameraLook : MonoBehaviour
{
    [Header("Sensibilidade")]
    [Tooltip("Graus de giro por pixel de movimento do mouse (independe do FPS). " +
             "0.12 = calmo, para uso sentado; 0.2 = rápido.")]
    [SerializeField, Min(0f)] private float mouseSensitivity = 0.12f;

    [Header("Peso / Inércia")]
    [Tooltip("Segundos para a câmera alcançar o alvo. 0.05 = rápido; 0.15 = cabeça pesada cinematográfica.")]
    [SerializeField, Min(0.01f)] private float smoothTime = 0.08f;

    [Header("Limites verticais (graus)")]
    [SerializeField] private float pitchMin = -45f;
    [SerializeField] private float pitchMax =  50f;

    [Header("Limites horizontais (graus)")]
    [SerializeField] private float yawMin = -60f;
    [SerializeField] private float yawMax =  60f;

    private InputAction _lookAction;
    private float _baseYaw;
    private bool  _isLookEnabled = true;

    // Alvo: onde o mouse quer que a câmera esteja (clampado)
    private float _targetPitch;
    private float _targetYaw;

    // Atual: posição real da câmera, perseguindo o alvo com inércia
    private float _currentPitch;
    private float _currentYaw;

    // Velocidades internas do SmoothDamp — nunca manipule diretamente
    private float _pitchVelocity;
    private float _yawVelocity;

    // ── Ciclo de vida ────────────────────────────────────────────────────────

    private void Awake()
    {
        _lookAction = new InputAction(
            name: "CameraLook",
            type: InputActionType.Value,
            expectedControlType: "Vector2"
        );
        _lookAction.AddBinding("<Mouse>/delta");
    }

    private void Start()
    {
        _baseYaw = transform.eulerAngles.y;
    }

    private void OnEnable()
    {
        _lookAction.Enable();
        InterrogationUIToggle.OnMenuToggled += HandleMenuToggled;
    }

    private void OnDisable()
    {
        _lookAction.Disable();
        InterrogationUIToggle.OnMenuToggled -= HandleMenuToggled;
    }

    private void OnDestroy() => _lookAction.Dispose();

    // ── Loop principal — três responsabilidades claras ───────────────────────

    private void Update()
    {
        // Com a ajuda aberta o mouse é do overlay, não da câmera.
        if (!_isLookEnabled || TutorialOverlay.AnyOpen) return;

        AccumulateInput();
        SmoothTowardTarget();
        ApplyRotation();
    }

    // ── Métodos privados ─────────────────────────────────────────────────────

    private void AccumulateInput()
    {
        Vector2 delta = _lookAction.ReadValue<Vector2>();

        // Sem Time.deltaTime: o delta do mouse JÁ é a distância percorrida no
        // quadro. Multiplicar pelo tempo do quadro deixava a sensibilidade
        // presa ao FPS — com o VSync (60 FPS) a câmera ficou ~6x mais sensível
        // do que a 350 FPS. Agora: graus por pixel, igual em qualquer máquina.
        _targetYaw   += delta.x * mouseSensitivity;
        _targetPitch -= delta.y * mouseSensitivity;

        // Clamp no alvo: respeita limites antes mesmo da câmera chegar lá
        _targetYaw   = Mathf.Clamp(_targetYaw,   yawMin,   yawMax);
        _targetPitch = Mathf.Clamp(_targetPitch, pitchMin, pitchMax);
    }

    private void SmoothTowardTarget()
    {
        _currentPitch = Mathf.SmoothDamp(_currentPitch, _targetPitch, ref _pitchVelocity, smoothTime);
        _currentYaw   = Mathf.SmoothDamp(_currentYaw,   _targetYaw,   ref _yawVelocity,   smoothTime);
    }

    private void ApplyRotation()
    {
        transform.rotation = Quaternion.Euler(_currentPitch, _baseYaw + _currentYaw, 0f);
    }

    private void HandleMenuToggled(bool menuOpen) => _isLookEnabled = !menuOpen;
}
