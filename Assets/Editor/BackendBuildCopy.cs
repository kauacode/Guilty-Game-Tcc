using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Põe o backend empacotado dentro de todo build de Windows, em
/// [pasta do jogo]/Backend/ — é de lá que o BackendLauncher o inicia.
///
/// O backend NÃO fica no repositório do jogo (são ~83 MB gerados): ele é
/// produzido no repositório irmão agent-orchestrator-api por
///     venv\Scripts\python.exe scripts\empacotar_backend.py
/// e este script só copia o resultado. Se ele não existir, o build para
/// ANTES de começar, com o comando acima na mensagem — melhor do que gerar um
/// jogo que abre e não tem detetive.
/// </summary>
public class BackendBuildCopy : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    public int callbackOrder => 0;

    public static string BackendDist => Path.GetFullPath(Path.Combine(
        Application.dataPath, "..", "..", "agent-orchestrator-api", "dist", "guilty-backend"));

    private static bool IsWindows(BuildReport report) =>
        report.summary.platform == BuildTarget.StandaloneWindows64 ||
        report.summary.platform == BuildTarget.StandaloneWindows;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (!IsWindows(report)) return;
        if (!File.Exists(Path.Combine(BackendDist, "guilty-backend.exe")))
        {
            throw new BuildFailedException(
                "Backend empacotado não encontrado em:\n  " + BackendDist +
                "\nGere com (na pasta agent-orchestrator-api):\n" +
                "  venv\\Scripts\\python.exe scripts\\empacotar_backend.py");
        }
    }

    public void OnPostprocessBuild(BuildReport report)
    {
        if (!IsWindows(report)) return;

        string target = Path.Combine(Path.GetDirectoryName(report.summary.outputPath), "Backend");
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        CopyDirectory(BackendDist, target);
        Debug.Log($"[Build] Backend copiado para {target}");
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(from))
            CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
    }
}
