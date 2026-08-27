using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Mouselook de primeira pessoa restrito para o suspeito sentado.
/// Usa acumuladores próprios de pitch/yaw — nunca lê transform.eulerAngles
/// em loop — para evitar o bug de wrap 0-360 dos Euler Angles do Unity.
/// Pausa automaticamente quando o painel de UI está aberto, via evento
/// estático de InterrogationUIToggle (acoplamento zero).
/// </summary>
public class PlayerCameraLook : MonoBehaviour
{
    [Header("Sensibilidade")]
    [SerializeField] private float sensitivity = 120f;

    [Header("Suavização")]
    [Tooltip("Quanto maior, mais rápido chega ao target. ~8 = responsivo mas orgânico.")]
    [SerializeField] private float smoothness = 8f;

    [Header("Limites verticais (graus)")]
    [SerializeField] private float pitchMin = -45f;   // olhar para cima
    [SerializeField] private float pitchMax =  65f;   // olhar para baixo (colo / mesa)

    [Header("Limites horizontais (graus)")]
    [SerializeField] private float yawMin = -60f;
    [SerializeField] private float yawMax =  60f;

    private InputAction lookAction;
    private float _pitch;
    private float _yaw;
    private float _baseYaw;
    private bool _isLookEnabled = true;

    private void Awake()
    {
        lookAction = new InputAction(name: "CameraLook", type: InputActionType.Value, expectedControlType: "Vector2");
        lookAction.AddBinding("<Mouse>/delta");
    }

    private void Start()
    {
        _baseYaw = transform.eulerAngles.y;
    }

    private void OnEnable()
    {
        lookAction.Enable();
        InterrogationUIToggle.OnMenuToggled += HandleMenuToggled;
    }

    private void OnDisable()
    {
        lookAction.Disable();
        InterrogationUIToggle.OnMenuToggled -= HandleMenuToggled;
    }

    private void OnDestroy()
    {
        lookAction.Dispose();
    }

    private void HandleMenuToggled(bool menuOpen)
    {
        _isLookEnabled = !menuOpen;
    }

    private void Update()
    {
        if (!_isLookEnabled)
        {
            return;
        }

        Vector2 delta = lookAction.ReadValue<Vector2>();

        _yaw   += delta.x * sensitivity * Time.deltaTime;
        _pitch -= delta.y * sensitivity * Time.deltaTime;

        _yaw   = Mathf.Clamp(_yaw,   yawMin,   yawMax);
        _pitch = Mathf.Clamp(_pitch, pitchMin, pitchMax);

        Quaternion target = Quaternion.Euler(_pitch, _baseYaw + _yaw, 0f);
        transform.rotation = Quaternion.Lerp(transform.rotation, target, smoothness * Time.deltaTime);
    }
}
