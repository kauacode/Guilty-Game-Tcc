using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Menu inicial. Só navegação — nenhuma regra de jogo mora aqui.
/// O botão de Configurações é placeholder visual: fica desabilitado e
/// anuncia isso, em vez de abrir uma tela vazia.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Header("Cena de jogo")]
    [SerializeField] private string gameScene = "SampleScene";

    [Header("Entrada")]
    [SerializeField] private CanvasGroup root;
    [SerializeField] private float fadeInDuration = 0.5f;

    private bool leaving;

    private void Start()
    {
        // o menu é o único lugar do jogo com cursor livre por padrão
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Time.timeScale = 1f;   // caso tenha vindo de um pause

        if (root != null)
        {
            root.alpha = 0f;
            StartCoroutine(FadeIn());
        }
    }

    private IEnumerator FadeIn()
    {
        float t = 0f;
        while (t < fadeInDuration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / fadeInDuration);
            root.alpha = k * k * (3f - 2f * k);
            yield return null;
        }
        root.alpha = 1f;
    }

    public void StartGame()
    {
        if (leaving) return;
        leaving = true;
        StartCoroutine(FadeOutThenLoad());
    }

    private IEnumerator FadeOutThenLoad()
    {
        float t = 0f, from = root != null ? root.alpha : 1f;
        while (t < 0.32f)
        {
            t += Time.unscaledDeltaTime;
            if (root != null) root.alpha = Mathf.Lerp(from, 0f, Mathf.Clamp01(t / 0.32f));
            yield return null;
        }
        SceneManager.LoadScene(gameScene);
    }

    public void QuitGame()
    {
        // No Editor o Application.Quit é ignorado, então avisamos no Console
        // para não parecer que o botão está quebrado durante os testes.
#if UNITY_EDITOR
        Debug.Log("[Menu] Sair: no Editor o Application.Quit não encerra nada. " +
                  "Funciona na build.");
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
