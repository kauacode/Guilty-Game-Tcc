using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Sons de interface do jogo inteiro: passar o mouse e clicar em qualquer
/// botão, abrir/fechar a prancheta (TAB), enviar o depoimento e a seleção de
/// casos.
///
/// Botões, opções e dropdowns são cobertos AUTOMATICAMENTE: a cada quadro ele
/// pergunta ao EventSystem o que está sob o mouse. Assim pega também a UI
/// criada em tempo de execução (tutorial, itens da lista do dropdown) e
/// qualquer tela nova, sem ninguém precisar ligar som botão a botão.
///
/// Os arquivos ficam em Resources/Audio/UI (pacotes CC0 da Kenney, ver
/// CREDITOS.txt). Volume: slider "Efeitos" das Configurações; o volume geral
/// já age sobre tudo via AudioListener.
/// </summary>
public class UiAudio : MonoBehaviour
{
    public enum Som { Hover, Clique, Opcao, PranchetaAbrir, PranchetaFechar, Enviar, DossieAbrir, DossieFechar, IniciarCaso }

    // arquivo em Resources/Audio/UI e volume relativo (os sons da Kenney vêm
    // bem mais altos que o clima do jogo pede; o hover é o mais discreto)
    private static readonly (Som som, string arquivo, float volume)[] Tabela =
    {
        (Som.Hover,           "ui_hover",          0.20f),
        (Som.Clique,          "ui_clique",         0.45f),
        (Som.Opcao,           "ui_opcao",          0.40f),
        (Som.PranchetaAbrir,  "prancheta_abrir",   0.55f),
        (Som.PranchetaFechar, "prancheta_fechar",  0.45f),
        (Som.Enviar,          "depoimento_enviar", 0.60f),
        (Som.DossieAbrir,     "dossie_abrir",      0.55f),
        (Som.DossieFechar,    "dossie_fechar",     0.45f),
        (Som.IniciarCaso,     "caso_iniciar",      0.60f),
    };

    private static UiAudio instance;

    private readonly Dictionary<Som, (AudioClip clip, float volume)> clips = new Dictionary<Som, (AudioClip, float)>();
    private readonly List<RaycastResult> acertos = new List<RaycastResult>();
    private AudioSource fonte;
    private PointerEventData ponteiro;
    private GameObject ultimoHover;
    private ApiClient apiAssinada;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        if (instance != null) return;
        var go = new GameObject("[UiAudio]");
        DontDestroyOnLoad(go);
        go.AddComponent<UiAudio>();
    }

    private void Awake()
    {
        instance = this;
        fonte = gameObject.AddComponent<AudioSource>();
        fonte.playOnAwake = false;
        fonte.spatialBlend = 0f;           // som de interface: sem posição no espaço
        fonte.ignoreListenerPause = true;  // o menu de pausa também faz som

        foreach (var (som, arquivo, volume) in Tabela)
        {
            var clip = Resources.Load<AudioClip>("Audio/UI/" + arquivo);
            if (clip != null) clips[som] = (clip, volume);
            else Debug.LogWarning($"[UiAudio] som não encontrado: Resources/Audio/UI/{arquivo}");
        }

        InterrogationUIToggle.OnMenuToggled += AoAlternarPrancheta;
    }

    private void OnDestroy()
    {
        InterrogationUIToggle.OnMenuToggled -= AoAlternarPrancheta;
        if (apiAssinada != null) apiAssinada.OnRequestStarted -= AoEnviar;
        if (instance == this) instance = null;
    }

    /// <summary>Toca um som de interface. Seguro de chamar de qualquer lugar.</summary>
    public static void Play(Som som)
    {
        if (instance == null || !instance.clips.TryGetValue(som, out var c)) return;
        GameSettings.Load();
        instance.fonte.PlayOneShot(c.clip, c.volume * GameSettings.Sfx);
    }

    private void AoAlternarPrancheta(bool aberta) => Play(aberta ? Som.PranchetaAbrir : Som.PranchetaFechar);
    private void AoEnviar() => Play(Som.Enviar);

    private void Update()
    {
        AssinarApiClient();
        DetectarHoverEClique();
    }

    // O ApiClient nasce na cena de jogo; assina quando ele aparecer.
    private void AssinarApiClient()
    {
        var api = ApiClient.Instance;
        if (api == apiAssinada) return;
        if (apiAssinada != null) apiAssinada.OnRequestStarted -= AoEnviar;
        apiAssinada = api;
        if (apiAssinada != null) apiAssinada.OnRequestStarted += AoEnviar;
    }

    private void DetectarHoverEClique()
    {
        var eventos = EventSystem.current;
        var mouse = Mouse.current;
        // Cursor preso no centro = olhando a sala, não usando a interface.
        if (eventos == null || mouse == null || Cursor.lockState == CursorLockMode.Locked)
        {
            ultimoHover = null;
            return;
        }

        ponteiro ??= new PointerEventData(eventos);
        ponteiro.position = mouse.position.ReadValue();
        acertos.Clear();
        eventos.RaycastAll(ponteiro, acertos);

        Selectable alvo = acertos.Count > 0 ? acertos[0].gameObject.GetComponentInParent<Selectable>() : null;
        // Campo de texto e barra de rolagem não são "botões": sem som.
        if (alvo is TMP_InputField || alvo is Scrollbar || (alvo != null && !alvo.IsInteractable()))
            alvo = null;

        var go = alvo != null ? alvo.gameObject : null;
        if (go != ultimoHover)
        {
            ultimoHover = go;
            if (go != null) Play(Som.Hover);
        }

        if (alvo != null && mouse.leftButton.wasPressedThisFrame)
            Play(alvo is Toggle || alvo is TMP_Dropdown || alvo is Slider ? Som.Opcao : Som.Clique);
    }
}
