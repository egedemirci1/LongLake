using System;
using UnityEngine;

/// <summary>
/// Sahne değişimlerinden bağımsız, kalıcı ses ayarları.
/// UI bu servisi kullanır; ses üreten sistemler kategori çarpanlarını uygular.
/// </summary>
public sealed class AudioSettingsService : MonoBehaviour
{
    private const string MasterKey = "Audio.Master";
    private const string MusicKey = "Audio.Music";
    private const string SfxKey = "Audio.Sfx";
    private const string AmbienceKey = "Audio.Ambience";

    public static AudioSettingsService Instance { get; private set; }
    public static event Action SettingsChanged;

    public static float MasterVolume { get; private set; } = 1f;
    public static float MusicVolume { get; private set; } = 1f;
    public static float SfxVolume { get; private set; } = 1f;
    public static float AmbienceVolume { get; private set; } = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null)
            return;

        var go = new GameObject("AudioSettingsService");
        DontDestroyOnLoad(go);
        go.AddComponent<AudioSettingsService>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        MasterVolume = PlayerPrefs.GetFloat(MasterKey, 1f);
        MusicVolume = PlayerPrefs.GetFloat(MusicKey, 1f);
        SfxVolume = PlayerPrefs.GetFloat(SfxKey, 1f);
        AmbienceVolume = PlayerPrefs.GetFloat(AmbienceKey, 1f);
        ApplyMaster();
    }

    public static void SetMasterVolume(float value)
    {
        MasterVolume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(MasterKey, MasterVolume);
        ApplyMaster();
        SettingsChanged?.Invoke();
    }

    public static void SetMusicVolume(float value)
    {
        MusicVolume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(MusicKey, MusicVolume);
        SettingsChanged?.Invoke();
    }

    public static void SetSfxVolume(float value)
    {
        SfxVolume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(SfxKey, SfxVolume);
        SettingsChanged?.Invoke();
    }

    public static void SetAmbienceVolume(float value)
    {
        AmbienceVolume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(AmbienceKey, AmbienceVolume);
        SettingsChanged?.Invoke();
    }

    public static float ScaleMusic(float baseVolume) =>
        Mathf.Clamp01(baseVolume) * MusicVolume;

    public static float ScaleSfx(float baseVolume) =>
        Mathf.Clamp01(baseVolume) * SfxVolume;

    public static float ScaleAmbience(float baseVolume) =>
        Mathf.Clamp01(baseVolume) * AmbienceVolume;

    private static void ApplyMaster()
    {
        AudioListener.volume = MasterVolume;
    }

    private void OnApplicationQuit()
    {
        PlayerPrefs.Save();
    }
}
