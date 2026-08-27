using System;
using TMPro;
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

    private const string PromptClosed = "[TAB] Abrir Interrogatório";
    private const string PromptOpen   = "[TAB] Fechar / [ENTER] Enviar";

    [Header("Painel a alternar")]
    [Tooltip("GameObject raiz do painel de chat. É desativado (SetActive(false)) 100% ao fechar — sem resíduo transparente, sem custo de raycast/render.")]
    [SerializeField] private GameObject chatPanelRoot;
    [Tooltip("CanvasGroup do próprio chatPanelRoot, usado apenas para o fade suave de abertura/fechamento.")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("HUD")]
    [Tooltip("Texto de instruções no canto da tela, atualizado dinamicamente conforme o painel abre/fecha.")]
    [SerializeField] private TMP_Text inputPromptText;

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

        // Ao abrir, reativa o GameObject ANTES do fade — precisa estar
        // ativo para renderizar e receber input durante a transição.
        if (visible && chatPanelRoot != null && !chatPanelRoot.activeSelf)
        {
            chatPanelRoot.SetActive(true);
        }

        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;

        Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = visible;

        if (inputPromptText != null)
        {
            inputPromptText.text = visible ? PromptOpen : PromptClosed;
        }

        OnMenuToggled?.Invoke(visible);

        if (instant)
        {
            canvasGroup.alpha = targetAlpha;

            if (!visible && chatPanelRoot != null)
            {
                chatPanelRoot.SetActive(false);
            }
        }
    }

    private void Update()
    {
        if (!Mathf.Approximately(canvasGroup.alpha, targetAlpha))
        {
            canvasGroup.alpha = Mathf.SmoothDamp(canvasGroup.alpha, targetAlpha, ref fadeVelocity, fadeDuration);
            return;
        }

        // Fade de saída concluído: desativa o GameObject por completo.
        // Isso garante zero resíduo transparente e remove o painel do
        // grafo de render/raycast enquanto estiver fechado.
        if (!isVisible && chatPanelRoot != null && chatPanelRoot.activeSelf)
        {
            chatPanelRoot.SetActive(false);
        }
    }
}
