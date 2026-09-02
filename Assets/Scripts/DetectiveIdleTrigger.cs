using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Disparo PROVISÓRIO de idle ocasional — existe só para dar o que testar em
/// Play mode. Não é o sistema definitivo de idles.
///
/// Sorteia um intervalo entre minInterval e maxInterval e dispara o trigger.
/// Quando o sistema real existir (provavelmente escolhendo entre cansaço /
/// intimidação / fumar conforme o estado do interrogatório), este componente sai.
///
/// Nota: o projeto está em activeInputHandler = 1 (só o Input System novo),
/// então a tecla de debug usa UnityEngine.InputSystem. O UnityEngine.Input
/// antigo lança exceção nesse modo.
/// </summary>
[RequireComponent(typeof(Animator))]
public class DetectiveIdleTrigger : MonoBehaviour
{
    [Header("Intervalo entre disparos (segundos)")]
    [SerializeField] private float minInterval = 4f;
    [SerializeField] private float maxInterval = 9f;

    [Header("Debug")]
    [Tooltip("Dispara na hora ao apertar T, para não ficar esperando o timer.")]
    [SerializeField] private bool allowManualKey = true;

    private static readonly int FingerTap = Animator.StringToHash("FingerTap");

    private Animator _animator;
    private float _next;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        ScheduleNext();
    }

    private void Update()
    {
        if (allowManualKey && DebugKeyPressed())
        {
            Fire();
            return;
        }

        if (Time.time >= _next) Fire();
    }

    private bool DebugKeyPressed()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        return kb != null && kb.tKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.T);
#else
        return false;
#endif
    }

    private void Fire()
    {
        _animator.SetTrigger(FingerTap);
        ScheduleNext();
    }

    private void ScheduleNext()
    {
        _next = Time.time + Random.Range(minInterval, maxInterval);
    }
}
