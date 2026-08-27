using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Alterna a visibilidade do painel de interrogatório (Canvas) via tecla,
/// usando uma InputAction própria (não depende do Input Action Asset
/// compartilhado do projeto, então não precisa editar esse asset).
/// Enquanto o painel está fechado, o cursor é travado/ocultado para
/// reforçar a imersão em 1ª pessoa; ao abrir, o cursor volta para permitir
/// clicar nos elementos da UI.
/// Expõe OnMenuToggled para que sistemas externos (ex: PlayerCameraLook)
/// pausem/retomem sem referenciar este script diretamente.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class InterrogationUIToggle : MonoBehaviour
{
    /// <summary>
    /// Disparado quando o painel abre (true) ou fecha (false).
    /// Assinantes não precisam de referência direta a este componente.
    /// </summary>
    public static event Action<bool> OnMenuToggled;

    [Header("Painel a alternar")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Configuração")]
    [SerializeField] private float fadeDuration = 0.2f;

    private InputAction toggleAction;
    private bool isVisible;
    private float targetAlpha;
    private float fadeVelocity;

    private void Awake()
    {
        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
        }

        toggleAction = new InputAction(name: "ToggleInterrogationUI", type: InputActionType.Button);
        toggleAction.AddBinding("<Keyboard>/tab");
        toggleAction.AddBinding("<Keyboard>/space");
        toggleAction.performed += OnTogglePerformed;
    }

    private void OnEnable()
    {
        toggleAction.Enable();
        SetVisible(false, instant: true);
    }

    private void OnDisable()
    {
        toggleAction.Disable();
    }

    private void OnDestroy()
    {
        toggleAction.performed -= OnTogglePerformed;
        toggleAction.Dispose();
    }

    private void OnTogglePerformed(InputAction.CallbackContext context)
    {
        SetVisible(!isVisible);
    }

    private void SetVisible(bool visible, bool instant = false)
    {
        isVisible = visible;
        targetAlpha = visible ? 1f : 0f;

        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;

        Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = visible;

        OnMenuToggled?.Invoke(visible);

        if (instant)
        {
            canvasGroup.alpha = targetAlpha;
        }
    }

    private void Update()
    {
        if (!Mathf.Approximately(canvasGroup.alpha, targetAlpha))
        {
            canvasGroup.alpha = Mathf.SmoothDamp(canvasGroup.alpha, targetAlpha, ref fadeVelocity, fadeDuration);
        }
    }
}
