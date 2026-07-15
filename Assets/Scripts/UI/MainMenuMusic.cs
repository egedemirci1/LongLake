using System.Collections;
using UnityEngine;

/// <summary>
/// Loops menu music with fade-in on enter and fade-out when leaving the menu.
/// </summary>
[RequireComponent(typeof(Transform))]
public class MainMenuMusic : MonoBehaviour
{
    [SerializeField] private AudioClip musicClip;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] [Range(0f, 1f)] private float targetVolume = 0.45f;
    [SerializeField] private float fadeInSeconds = 2f;
    [SerializeField] private float fadeOutSeconds = 1.25f;

    private Coroutine fadeRoutine;
    private bool fadingOut;

    private void Awake()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        audioSource.playOnAwake = false;
        audioSource.loop = true;
        audioSource.spatialBlend = 0f;
        audioSource.volume = 0f;

        if (musicClip != null)
            audioSource.clip = musicClip;
    }

    private void Start()
    {
        if (audioSource.clip == null)
        {
            Debug.LogError("[MainMenuMusic] Assign longlake-loop.mp3 (AudioClip).");
            return;
        }

        audioSource.Play();
        StartFade(targetVolume, fadeInSeconds);
    }

    public void FadeOut()
    {
        if (fadingOut || audioSource == null || !audioSource.isPlaying)
            return;

        fadingOut = true;
        StartFade(0f, fadeOutSeconds, stopWhenDone: true);
    }

    private void StartFade(float toVolume, float duration, bool stopWhenDone = false)
    {
        if (fadeRoutine != null)
            StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeRoutine(toVolume, duration, stopWhenDone));
    }

    private IEnumerator FadeRoutine(float toVolume, float duration, bool stopWhenDone)
    {
        float from = audioSource.volume;
        float t = 0f;
        duration = Mathf.Max(0.01f, duration);

        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / duration);
            audioSource.volume = Mathf.Lerp(from, toVolume, u);
            yield return null;
        }

        audioSource.volume = toVolume;
        if (stopWhenDone)
            audioSource.Stop();

        fadeRoutine = null;
    }

    private void OnDestroy()
    {
        if (audioSource != null && audioSource.isPlaying)
            audioSource.Stop();
    }
}
