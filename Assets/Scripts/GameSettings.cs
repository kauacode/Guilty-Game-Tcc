using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Estado das configurações do jogador: volumes, resolução e tela cheia.
///
/// Fica separado da UI de propósito — a tela de Configurações só lê e escreve
/// aqui. Assim o Pause pode abrir a mesma tela depois sem duplicar lógica, e as
/// preferências são aplicadas no boot do jogo mesmo sem nenhuma tela aberta.
///
/// ÁUDIO: o projeto NÃO tinha nenhum som quando isto foi escrito — nenhum
/// AudioMixer, nenhum .wav/.mp3/.ogg, e o único AudioSource da cena (o zumbido
/// da luminária, em InterrogationLightFlicker) está sem clipe. Os sliders estão
/// corretamente ligados, mas não haverá nada audível até que sons sejam
/// adicionados. Ver o relatório para o que plugar depois.
///
/// Se existir um AudioMixer com os parâmetros expostos, ele é usado (é o jeito
/// certo: os AudioSources roteiam por grupos). Se não existir, cai para
/// AudioListener.volume no volume geral — que funciona sozinho e não deixa o
/// slider mentindo para o jogador.
/// </summary>
public static class GameSettings
{
    public const string MixerResourcePath = "Audio/GuiltyMixer";

    // parâmetros expostos esperados no mixer
    public const string ParamMaster = "MasterVolume";
    public const string ParamMusic  = "MusicVolume";
    public const string ParamSfx    = "SfxVolume";

    private const string KeyMaster = "guilty.vol.master";
    private const string KeyMusic  = "guilty.vol.music";
    private const string KeySfx    = "guilty.vol.sfx";
    private const string KeyResW   = "guilty.screen.w";
    private const string KeyResH   = "guilty.screen.h";
    private const string KeyFull   = "guilty.screen.fullscreen";

    public static float Master { get; private set; } = 0.8f;
    public static float Music  { get; private set; } = 0.8f;
    public static float Sfx    { get; private set; } = 0.8f;
    public static bool  Fullscreen { get; private set; } = true;

    private static AudioMixer mixer;
    private static bool loaded;

    /// <summary>Aplica o que estiver salvo assim que o jogo sobe, em qualquer cena.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        Load();
        ApplyAudio();
        // resolução não é reaplicada no boot de propósito: no Editor isso não faz
        // nada e numa build a Unity já restaura a última resolução usada. Reaplicar
        // aqui causaria um flicker de janela na abertura.
    }

    public static void Load()
    {
        if (loaded) return;
        Master = PlayerPrefs.GetFloat(KeyMaster, 0.8f);
        Music  = PlayerPrefs.GetFloat(KeyMusic,  0.8f);
        Sfx    = PlayerPrefs.GetFloat(KeySfx,    0.8f);
        Fullscreen = PlayerPrefs.GetInt(KeyFull, Screen.fullScreen ? 1 : 0) == 1;
        loaded = true;
    }

    public static void Save()
    {
        PlayerPrefs.SetFloat(KeyMaster, Master);
        PlayerPrefs.SetFloat(KeyMusic,  Music);
        PlayerPrefs.SetFloat(KeySfx,    Sfx);
        PlayerPrefs.SetInt(KeyFull, Fullscreen ? 1 : 0);
        PlayerPrefs.SetInt(KeyResW, Screen.width);
        PlayerPrefs.SetInt(KeyResH, Screen.height);
        PlayerPrefs.Save();
    }

    public static Vector2Int SavedResolution =>
        new Vector2Int(PlayerPrefs.GetInt(KeyResW, Screen.width),
                       PlayerPrefs.GetInt(KeyResH, Screen.height));

    // ─────────────────────────────── áudio ───────────────────────────────

    public static void SetMaster(float v) { Master = Mathf.Clamp01(v); ApplyAudio(); }
    public static void SetMusic (float v) { Music  = Mathf.Clamp01(v); ApplyAudio(); }
    public static void SetSfx   (float v) { Sfx    = Mathf.Clamp01(v); ApplyAudio(); }

    private static AudioMixer Mixer
    {
        get
        {
            if (mixer == null) mixer = Resources.Load<AudioMixer>(MixerResourcePath);
            return mixer;
        }
    }

    /// <summary>
    /// Só conta como "tem mixer" se o parâmetro exposto REALMENTE existir.
    ///
    /// Um AudioMixer criado mas sem expor MasterVolume aceitaria o SetFloat
    /// silenciosamente sem fazer nada — e, pior, desligaria o fallback do
    /// AudioListener, deixando o slider de volume sem efeito algum. Checar o
    /// parâmetro evita esse meio-termo.
    /// </summary>
    public static bool HasMixer => Mixer != null && Mixer.GetFloat(ParamMaster, out _);

    private static void ApplyAudio()
    {
        var m = HasMixer ? Mixer : null;
        if (m != null)
        {
            m.SetFloat(ParamMaster, ToDb(Master));
            m.SetFloat(ParamMusic,  ToDb(Music));
            m.SetFloat(ParamSfx,    ToDb(Sfx));
        }
        else
        {
            // sem mixer o volume geral ainda funciona de verdade
            AudioListener.volume = Master;
        }
    }

    /// <summary>
    /// Slider linear (0..1) para decibéis. Volume percebido é logarítmico: sem
    /// esta conversão, metade do slider soaria quase igual ao máximo.
    /// </summary>
    private static float ToDb(float linear)
    {
        return linear <= 0.0001f ? -80f : Mathf.Log10(Mathf.Clamp01(linear)) * 20f;
    }

    // ────────────────────────────── resolução ──────────────────────────────

    /// <summary>
    /// Resoluções suportadas pelo monitor, sem repetir a mesma largura×altura
    /// em taxas de atualização diferentes (o Screen.resolutions lista uma
    /// entrada por refresh rate, o que encheria o dropdown de duplicatas).
    /// </summary>
    public static List<Vector2Int> AvailableResolutions()
    {
        var seen = new HashSet<long>();
        var list = new List<Vector2Int>();

        foreach (var r in Screen.resolutions)
        {
            long key = ((long)r.width << 32) | (uint)r.height;
            if (seen.Add(key)) list.Add(new Vector2Int(r.width, r.height));
        }

        // no Editor o Screen.resolutions costuma vir vazio ou com uma entrada só
        if (list.Count == 0)
        {
            list.AddRange(new[]
            {
                new Vector2Int(1280, 720), new Vector2Int(1600, 900),
                new Vector2Int(1920, 1080), new Vector2Int(2560, 1440),
            });
        }

        list.Sort((a, b) => (a.x * a.y).CompareTo(b.x * b.y));
        return list;
    }

    public static void ApplyResolution(Vector2Int res, bool fullscreen)
    {
        Fullscreen = fullscreen;
        // FullScreenWindow (borderless) é o modo que o projeto já usa em
        // ProjectSettings (fullscreenMode: 1) — manter é menos surpresa.
        Screen.SetResolution(res.x, res.y,
            fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
    }

    public static void SetFullscreen(bool on)
    {
        Fullscreen = on;
        Screen.fullScreenMode = on ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        Screen.fullScreen = on;
    }
}
