using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Máquina de estados do papel amassado da prancheta.
///
/// O papel alterna entre DOIS modos, nunca os dois ao mesmo tempo:
///   • LEITURA  — mostra a fala do detetive; o botão diz "RESPONDER".
///   • ESCRITA  — mostra o campo de digitação; o botão diz "ENVIAR".
///
/// Este script cuida SÓ dessa alternância e do rótulo do botão. Quem envia o
/// depoimento, aplica regra de jogo, mexe na barra de suspeita e trata fim de
/// jogo continua sendo o UIController — 331 linhas já testadas que não faz
/// sentido reescrever.
///
/// A ponte entre os dois é o <see cref="sendProxy"/>: um Button escondido que o
/// UIController escuta como se fosse o botão de enviar. Quando o jogador clica
/// no ENVIAR visível estando em modo escrita, este script dispara o proxy e o
/// UIController faz o resto. É isso que evita o bug óbvio da abordagem direta:
/// se o UIController escutasse o botão visível, clicar em "RESPONDER" (campo
/// vazio) cairia na validação dele e sobrescreveria a fala do detetive com
/// "[Sistema] Digite algo antes de enviar."
/// </summary>
public class InterrogationMenuController : MonoBehaviour
{
    [Header("Áreas do papel (alternam entre si)")]
    [SerializeField] private GameObject detectiveArea;
    [SerializeField] private GameObject writingArea;
    [SerializeField] private TMP_InputField statementInputField;

    [Header("Botão")]
    [SerializeField] private Button submitButton;
    [SerializeField] private TMP_Text submitLabel;
    [Tooltip("Button escondido que o UIController escuta. Disparado por código " +
             "quando o envio é legítimo. Se ficar vazio, o menu funciona como " +
             "demo isolada (alterna os modos, mas não envia nada).")]
    [SerializeField] private Button sendProxy;

    private const string LabelWriting = "ENVIAR";
    private const string LabelReading = "RESPONDER";

    private bool isReading;

    private void Start()
    {
        if (detectiveArea == null || writingArea == null ||
            statementInputField == null || submitButton == null)
        {
            Debug.LogError("[InterrogationMenu] Referências não atribuídas — " +
                           "rode 'Guilty › Menu Interrogatório - Integrar na cena'.", this);
            enabled = false;
            return;
        }

        submitButton.onClick.AddListener(OnSubmitClicked);

        if (ApiClient.Instance != null)
        {
            ApiClient.Instance.OnRequestStarted  += HandleRequestStarted;
            ApiClient.Instance.OnRequestFinished += HandleRequestFinished;
            ApiClient.Instance.OnResponseReceived += HandleResponse;
            ApiClient.Instance.OnError            += HandleError;
        }

        // Abre em leitura: o UIController já escreveu a fala de abertura no
        // detectiveText durante o Start dele.
        SetReading(true);
    }

    private void OnDestroy()
    {
        // Remove só o que este script registrou — o UIController tem os
        // próprios listeners no proxy e não pode ser desconectado junto.
        if (submitButton != null)
            submitButton.onClick.RemoveListener(OnSubmitClicked);

        if (ApiClient.Instance != null)
        {
            ApiClient.Instance.OnRequestStarted  -= HandleRequestStarted;
            ApiClient.Instance.OnRequestFinished -= HandleRequestFinished;
            ApiClient.Instance.OnResponseReceived -= HandleResponse;
            ApiClient.Instance.OnError            -= HandleError;
        }
    }

    // ─── Alternância ─────────────────────────────────────────────────────────

    private void SetReading(bool reading)
    {
        isReading = reading;

        detectiveArea.SetActive(reading);
        writingArea.SetActive(!reading);

        if (submitLabel != null)
            submitLabel.text = reading ? LabelReading : LabelWriting;

        if (!reading)
        {
            statementInputField.text = string.Empty;
            statementInputField.ActivateInputField();
        }
    }

    private void OnSubmitClicked()
    {
        if (isReading)
        {
            SetReading(false);          // "RESPONDER" → abre o campo
            return;
        }

        if (string.IsNullOrWhiteSpace(statementInputField.text))
        {
            // Campo vazio não vira turno. Não delega ao UIController porque a
            // mensagem de aviso dele iria para o detectiveText, que está oculto
            // neste modo — o jogador não veria nada e acharia que travou.
            statementInputField.ActivateInputField();
            return;
        }

        // O proxy carrega o listener do UIController, que lê o MESMO
        // InputField (ele aponta para este) e dispara a chamada à API.
        if (sendProxy != null) sendProxy.onClick.Invoke();
        else Debug.LogWarning("[InterrogationMenu] sendProxy vazio — nada foi enviado.", this);
    }

    // ─── Reações à API ───────────────────────────────────────────────────────

    private void HandleRequestStarted()
    {
        // Volta para leitura: o UIController escreve "O detetive está
        // analisando seu depoimento..." no detectiveText.
        SetReading(true);
        submitButton.interactable = false;
    }

    private void HandleRequestFinished()
    {
        // Espera um frame antes de reabilitar. O UIController também reage a
        // este evento e pode DESABILITAR o proxy no mesmo frame (fim de jogo).
        // Lendo o estado do proxy no frame seguinte, o botão visível herda a
        // decisão dele em vez de competir com ela.
        StartCoroutine(SyncInteractableNextFrame());
    }

    private IEnumerator SyncInteractableNextFrame()
    {
        yield return null;
        submitButton.interactable = sendProxy == null || sendProxy.interactable;
    }

    private void HandleResponse(AnalyzeResponse response) => SetReading(true);

    private void HandleError(string message) => SetReading(true);

    // ─── API pública (para outros sistemas, ex.: timeout de turno) ───────────

    /// <summary>Força o papel de volta ao modo de escrita.</summary>
    public void OpenWriting() => SetReading(false);

    /// <summary>Força o papel de volta ao modo de leitura.</summary>
    public void OpenReading() => SetReading(true);
}
