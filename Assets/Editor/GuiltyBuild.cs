using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Gera o jogo para Windows em Builds/Windows/Guilty.exe (com o backend em
/// Builds/Windows/Backend/, via BackendBuildCopy) e o zip para distribuir em
/// Builds/Guilty-Windows-v[versão].zip.
///
/// Pelo editor: menu Guilty > Build - Gerar jogo (Windows).
/// Sem abrir o editor (o Unity precisa estar FECHADO para este projeto):
///   "C:\Program Files\Unity\Hub\Editor\6000.3.13f1\Editor\Unity.exe" -batchmode -quit
///     -projectPath . -executeMethod GuiltyBuild.BuildWindows -logFile build.log
///
/// A pasta Builds/ está no .gitignore: o jogo gerado vai para o GitHub
/// Releases, nunca para o repositório.
/// </summary>
public static class GuiltyBuild
{
    public static string OutputDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "Windows"));

    [MenuItem("Guilty/Build - Gerar jogo (Windows)")]
    public static void BuildWindows()
    {
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = Path.Combine(OutputDir, "Guilty.exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };

        var report = BuildPipeline.BuildPlayer(options);
        var s = report.summary;
        if (s.result != BuildResult.Succeeded)
        {
            Debug.LogError($"[Build] FALHOU ({s.result}) com {s.totalErrors} erro(s).");
            if (Application.isBatchMode) EditorApplication.Exit(1);
            return;
        }

        Debug.Log($"[Build] OK: {options.locationPathName} ({s.totalSize / (1024 * 1024)} MB, {s.totalTime.TotalSeconds:0}s)");

        string zip = PackageForRelease();
        Debug.Log($"[Build] Pacote para o GitHub Releases: {zip}");
    }

    /// <summary>
    /// Zip do que o jogador baixa: Guilty.exe, dados do Unity e Backend/.
    /// Os modelos de IA NÃO entram — são baixados pelo próprio jogo.
    /// </summary>
    private static string PackageForRelease()
    {
        // Símbolos de depuração do Burst: o próprio Unity avisa "DoNotShip".
        foreach (var dir in Directory.GetDirectories(OutputDir, "*_DoNotShip"))
            Directory.Delete(dir, recursive: true);

        string zip = Path.Combine(Path.GetDirectoryName(OutputDir), $"Guilty-Windows-v{PlayerSettings.bundleVersion}.zip");
        if (File.Exists(zip)) File.Delete(zip);
        System.IO.Compression.ZipFile.CreateFromDirectory(OutputDir, zip,
            System.IO.Compression.CompressionLevel.Optimal, includeBaseDirectory: false);
        return zip;
    }
}
