using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

/// <summary>
/// Sobe o backend (FastAPI + IA) sozinho quando o jogo abre e o encerra ao
/// sair. O jogador nunca precisa abrir terminal nem servidor.
///
/// Onde ele procura o backend:
///   BUILD  → [pasta do jogo]/Backend/guilty-backend.exe (copiado no build por
///            BackendBuildCopy). Modelos baixados vão para a pasta de dados do
///            usuário (Application.persistentDataPath/models).
///   EDITOR → o código-fonte em ../agent-orchestrator-api, via venv (sempre o
///            código mais recente, lendo o .env de desenvolvimento). Se não
///            houver venv, usa o executável de dist/.
///
/// Se já existir um backend respondendo na porta (ex.: alguém subiu o
/// servidor à mão para depurar), ele é reaproveitado e não é encerrado.
///
/// Criado automaticamente antes da primeira cena — não precisa estar em
/// nenhuma cena.
/// </summary>
public class BackendLauncher : MonoBehaviour
{
    public enum State { Starting, Ready, Failed }

    public const int Port = 8000;
    public static string BaseUrl => $"http://127.0.0.1:{Port}";

    public static BackendLauncher Instance { get; private set; }

    public State Status { get; private set; } = State.Starting;
    public string Error { get; private set; }
    public event Action<State> OnStateChanged;

    // 60s cobre antivírus inspecionando o .exe na primeira abertura, que é o
    // caso lento de verdade; uma abertura normal leva ~1-2s.
    private const float StartupTimeout = 60f;

    private Process process;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("[BackendLauncher]");
        DontDestroyOnLoad(go);
        go.AddComponent<BackendLauncher>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private IEnumerator Start()
    {
        bool? alreadyUp = null;
        yield return CheckHealth(ok => alreadyUp = ok);
        if (alreadyUp == true)
        {
            Debug.Log($"[Backend] Já havia um backend em {BaseUrl}; reaproveitando.");
            SetState(State.Ready);
            yield break;
        }

        string error = TryStartProcess();
        if (error != null) { Fail(error); yield break; }

        float deadline = Time.realtimeSinceStartup + StartupTimeout;
        while (Time.realtimeSinceStartup < deadline)
        {
            if (process.HasExited)
            {
                // Causa mais comum: outro programa já usa a porta, e o
                // /health acima não o reconheceu como nosso.
                Fail($"O detetive (backend) fechou ao iniciar (código {process.ExitCode}). " +
                     $"Verifique se outro programa está usando a porta {Port}.");
                yield break;
            }

            bool? up = null;
            yield return CheckHealth(ok => up = ok);
            if (up == true)
            {
                Debug.Log($"[Backend] Pronto em {BaseUrl}.");
                SetState(State.Ready);
                yield break;
            }
            yield return new WaitForSecondsRealtime(0.5f);
        }

        Fail($"O detetive (backend) não respondeu em {StartupTimeout:0}s.");
    }

    /// <summary>Para outros scripts esperarem o backend antes de chamar a API.</summary>
    public IEnumerator WaitUntilSettled()
    {
        while (Status == State.Starting) yield return null;
    }

    // ─────────────────────────────── processo ───────────────────────────────

    private string TryStartProcess()
    {
        var info = LocateBackend();
        if (info == null)
        {
#if UNITY_EDITOR
            return "Backend não encontrado. Crie o venv em agent-orchestrator-api " +
                   "(run_local.bat) ou gere o executável (scripts/empacotar_backend.py).";
#else
            return "Arquivos do detetive não encontrados (pasta Backend). Reinstale o jogo.";
#endif
        }

        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.EnvironmentVariables["GUILTY_PORT"] = Port.ToString();
        info.EnvironmentVariables["GUILTY_PARENT_PID"] = Process.GetCurrentProcess().Id.ToString();

        try
        {
            process = Process.Start(info);
            Debug.Log($"[Backend] Iniciado: {info.FileName} {info.Arguments} (pid {process.Id})");
            return null;
        }
        catch (Exception e)
        {
            return $"Não foi possível iniciar o detetive (backend): {e.Message}";
        }
    }

    private static ProcessStartInfo LocateBackend()
    {
#if UNITY_EDITOR
        // Assets/ → Guilty-Game-Tcc/ → Guilty/ → agent-orchestrator-api/
        string api = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "agent-orchestrator-api"));
        string venvPython = Path.Combine(api, "venv", "Scripts", "python.exe");
        if (File.Exists(venvPython))
        {
            // cwd = pasta da API: lê o .env de dev e usa os modelos de lá.
            return new ProcessStartInfo(venvPython, "run_server.py") { WorkingDirectory = api };
        }
        string devExe = Path.Combine(api, "dist", "guilty-backend", "guilty-backend.exe");
        if (File.Exists(devExe)) return PackagedStartInfo(devExe);
        return null;
#else
        string exe = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Backend", "guilty-backend.exe");
        return File.Exists(exe) ? PackagedStartInfo(exe) : null;
#endif
    }

    private static ProcessStartInfo PackagedStartInfo(string exe)
    {
        // Dados graváveis do usuário: a pasta do jogo pode estar em
        // "Arquivos de Programas", onde não se pode escrever.
        string dataDir = Path.Combine(Application.persistentDataPath, "backend");
        Directory.CreateDirectory(dataDir);

        var info = new ProcessStartInfo(exe) { WorkingDirectory = dataDir };
        info.EnvironmentVariables["GUILTY_MODELS_DIR"] = Path.Combine(Application.persistentDataPath, "models");
        return info;
    }

    private void OnApplicationQuit() => StopBackend();
    private void OnDestroy() { if (Instance == this) StopBackend(); }

    private void StopBackend()
    {
        if (process == null) return;
        try
        {
            if (!process.HasExited)
            {
                // /T derruba a árvore: o python.exe do venv é só um lançador que
                // abre o interpretador real como filho — Kill() mataria só o pai.
                var kill = Process.Start(new ProcessStartInfo("taskkill", $"/PID {process.Id} /T /F")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                kill?.WaitForExit(3000);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Backend] Falha ao encerrar o backend: {e.Message}");
        }
        process = null;
    }

    // ─────────────────────────────── saúde ───────────────────────────────

    [Serializable]
    private class Health { public string status; public string servico; }

    private static IEnumerator CheckHealth(Action<bool> done)
    {
        using (var req = UnityWebRequest.Get($"{BaseUrl}/health"))
        {
            req.timeout = 2;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success) { done(false); yield break; }

            // Confere que é o NOSSO backend, e não outro programa na mesma porta.
            Health h = null;
            try { h = JsonUtility.FromJson<Health>(req.downloadHandler.text); } catch { }
            done(h != null && h.servico == "guilty-backend");
        }
    }

    private void SetState(State s)
    {
        Status = s;
        OnStateChanged?.Invoke(s);
    }

    private void Fail(string message)
    {
        Error = message;
        Debug.LogError($"[Backend] {message}");
        SetState(State.Failed);
    }
}
