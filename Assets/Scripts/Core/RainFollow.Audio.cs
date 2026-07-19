using UnityEngine;

// Partial: split for maintainability. Type identity unchanged.
public partial class RainFollow : MonoBehaviour
{
    private void ConfigureRainAudio()
    {
        if (rainLoopClip == null)
            rainLoopClip = Resources.Load<AudioClip>(rainResourcesPath);
        if (rainLoopClip == null)
        {
            Debug.LogWarning($"[Rain] Audio clip bulunamadı: Resources/{rainResourcesPath}");
            return;
        }

        _rainAudio = GetComponent<AudioSource>();
        if (_rainAudio == null)
            _rainAudio = gameObject.AddComponent<AudioSource>();

        _rainAudio.clip = rainLoopClip;
        _rainAudio.loop = true;
        _rainAudio.playOnAwake = false;
        _rainAudio.spatialBlend = 0f;
        _rainAudio.volume = AudioSettingsService.ScaleAmbience(rainVolume);
        _rainAudio.dopplerLevel = 0f;
        _rainAudio.Play();
    }

}
