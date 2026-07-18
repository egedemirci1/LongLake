using UnityEngine;

/// <summary>
/// Global one-shot SFX player. Loads clips from Resources/SFX/.
/// Per-clip volumes are tuned from measured mean loudness (ffmpeg volumedetect)
/// so UI, world, character and reward SFX sit in a consistent mix.
/// </summary>
public class GameAudio : MonoBehaviour
{
    public static GameAudio Instance { get; private set; }

    // Target mean loudness (dBFS) by category → volume = 10^((target - measuredMean)/20), clamped.
    // Measured means (dB): jump ~-13, quest ~-18, door-close ~-19, inv ~-22,
    // pant ~-27, click ~-29, knock/open ~-35, pickup/craft ~-38, walk ~-60.
    private const float VolItemPickup = 1.00f;      // quiet file → full
    private const float VolDoorKnock = 0.95f;       // quiet file, slight headroom
    private const float VolDoorOpen = 0.90f;        // quiet file
    private const float VolDoorClose = 0.35f;       // hot file (−19 dB mean)
    private const float VolFemalePant = 0.40f;      // keep under footsteps/dialogue bed
    private const float VolMalePant = 0.40f;
    private const float VolInventoryOpen = 0.38f;   // hot UI (−22 dB)
    private const float VolInventoryClose = 0.36f;  // hot + clipped peak
    private const float VolButtonClick = 0.55f;     // clear but polite UI
    private const float VolCrafting = 1.00f;        // quiet file
    private const float VolFemaleJump = 0.22f;      // very hot (−14.5 dB, peaks 0)
    private const float VolMaleJump = 0.18f;        // hottest (−12.6 dB)
    private const float VolFemaleLanding = 0.42f;
    private const float VolMaleLanding = 0.30f;     // hotter than female landing
    private const float VolQuestStart = 0.48f;      // reward stinger, present
    private const float VolQuestFinish = 0.52f;     // slightly above start
    private const float VolDropItem = 0.45f;        // mid feedback (−25 dB mean)

    [Header("Clips (optional overrides)")]
    [SerializeField] private AudioClip itemPickupClip;
    [SerializeField] private AudioClip doorKnockClip;
    [SerializeField] private AudioClip doorOpenClip;
    [SerializeField] private AudioClip doorCloseClip;
    [SerializeField] private AudioClip femalePantingClip;
    [SerializeField] private AudioClip malePantingClip;
    [SerializeField] private AudioClip inventoryOpenClip;
    [SerializeField] private AudioClip inventoryCloseClip;
    [SerializeField] private AudioClip buttonClickClip;
    [SerializeField] private AudioClip craftingClip;
    [SerializeField] private AudioClip jumpingClip;
    [SerializeField] private AudioClip femaleJumpClip;
    [SerializeField] private AudioClip maleJumpClip;
    [SerializeField] private AudioClip femaleLandingClip;
    [SerializeField] private AudioClip maleLandingClip;
    [SerializeField] private AudioClip questStartClip;
    [SerializeField] private AudioClip questFinishClip;
    [SerializeField] private AudioClip dropItemClip;

    private AudioSource _source;
    private AudioSource _doorOpenSource;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;

        var go = new GameObject("GameAudio");
        DontDestroyOnLoad(go);
        go.AddComponent<GameAudio>();
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

        _source = gameObject.AddComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.spatialBlend = 0f;
        _source.volume = 1f;

        _doorOpenSource = gameObject.AddComponent<AudioSource>();
        _doorOpenSource.playOnAwake = false;
        _doorOpenSource.spatialBlend = 0f;
        _doorOpenSource.volume = 1f;

        if (itemPickupClip == null)
            itemPickupClip = Resources.Load<AudioClip>("SFX/item-pickup");
        if (doorKnockClip == null)
            doorKnockClip = Resources.Load<AudioClip>("SFX/door-knock");
        if (doorOpenClip == null)
            doorOpenClip = Resources.Load<AudioClip>("SFX/door-open");
        if (doorCloseClip == null)
            doorCloseClip = Resources.Load<AudioClip>("SFX/door-close");
        if (femalePantingClip == null)
            femalePantingClip = Resources.Load<AudioClip>("SFX/female-panting");
        if (malePantingClip == null)
            malePantingClip = Resources.Load<AudioClip>("SFX/male-panting");
        if (inventoryOpenClip == null)
            inventoryOpenClip = Resources.Load<AudioClip>("SFX/inventory-open");
        if (inventoryCloseClip == null)
            inventoryCloseClip = Resources.Load<AudioClip>("SFX/inventory-close");
        if (buttonClickClip == null)
            buttonClickClip = Resources.Load<AudioClip>("SFX/button-click");
        if (craftingClip == null)
            craftingClip = Resources.Load<AudioClip>("SFX/crafting");
        if (jumpingClip == null)
            jumpingClip = Resources.Load<AudioClip>("SFX/jumping");
        if (femaleJumpClip == null)
            femaleJumpClip = Resources.Load<AudioClip>("SFX/female-jump");
        if (maleJumpClip == null)
            maleJumpClip = Resources.Load<AudioClip>("SFX/male-jump");
        if (femaleLandingClip == null)
            femaleLandingClip = Resources.Load<AudioClip>("SFX/female-landing");
        if (maleLandingClip == null)
            maleLandingClip = Resources.Load<AudioClip>("SFX/male-landing");
        if (questStartClip == null)
            questStartClip = Resources.Load<AudioClip>("SFX/quest-start");
        if (questFinishClip == null)
            questFinishClip = Resources.Load<AudioClip>("SFX/quest-finish");
        if (dropItemClip == null)
            dropItemClip = Resources.Load<AudioClip>("SFX/dropping-item");
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void PlayItemPickup() => PlayOneShot(itemPickupClip, VolItemPickup);
    public void PlayDoorKnock() => PlayOneShot(doorKnockClip, VolDoorKnock);

    public void PlayDoorOpen()
    {
        if (_doorOpenSource == null || doorOpenClip == null) return;
        _doorOpenSource.clip = doorOpenClip;
        _doorOpenSource.volume = AudioSettingsService.ScaleSfx(VolDoorOpen);
        _doorOpenSource.Play();
    }

    public void PlayDoorClose()
    {
        if (_doorOpenSource != null && _doorOpenSource.isPlaying)
            _doorOpenSource.Stop();
        PlayOneShot(doorCloseClip, VolDoorClose);
    }

    public void PlayFemalePanting() => PlayOneShot(femalePantingClip, VolFemalePant);
    public void PlayMalePanting() => PlayOneShot(malePantingClip, VolMalePant);
    public void PlayInventoryOpen() => PlayOneShot(inventoryOpenClip, VolInventoryOpen);
    public void PlayInventoryClose() => PlayOneShot(inventoryCloseClip, VolInventoryClose);
    public void PlayButtonClick() => PlayOneShot(buttonClickClip, VolButtonClick);
    public void PlayCrafting() => PlayOneShot(craftingClip, VolCrafting);
    public void PlayDropItem() => PlayOneShot(dropItemClip, VolDropItem);

    public void PlayJumping(int characterIndex = -1)
    {
        AudioClip jumpClip = null;
        AudioClip landingClip = null;
        float jumpVol = 1f;
        float landVol = 1f;

        if (characterIndex == 0) // Ahu
        {
            jumpClip = femaleJumpClip;
            landingClip = femaleLandingClip;
            jumpVol = VolFemaleJump;
            landVol = VolFemaleLanding;
        }
        else if (characterIndex == 1) // Yaman
        {
            jumpClip = maleJumpClip;
            landingClip = maleLandingClip;
            jumpVol = VolMaleJump;
            landVol = VolMaleLanding;
        }
        else
        {
            jumpClip = jumpingClip;
            jumpVol = VolMaleJump;
        }

        if (jumpClip != null)
            PlayOneShot(jumpClip, jumpVol);

        if (landingClip != null)
            StartCoroutine(PlayLandingDelayed(landingClip, landVol, 0.75f));
    }

    private System.Collections.IEnumerator PlayLandingDelayed(AudioClip clip, float volume, float delay)
    {
        yield return new WaitForSeconds(delay);
        PlayOneShot(clip, volume);
    }

    public void PlayQuestStart() => PlayOneShot(questStartClip, VolQuestStart);
    public void PlayQuestFinish() => PlayOneShot(questFinishClip, VolQuestFinish);

    public void PlayOneShot(AudioClip clip, float volume = 1f)
    {
        if (clip == null || _source == null) return;
        _source.PlayOneShot(clip, AudioSettingsService.ScaleSfx(volume));
    }

    public static void PlayPickup()
    {
        if (Instance != null) Instance.PlayItemPickup();
    }

    public static void PlayKnock()
    {
        if (Instance != null) Instance.PlayDoorKnock();
    }

    public static void PlayOpen()
    {
        if (Instance != null) Instance.PlayDoorOpen();
    }

    public static void PlayClose()
    {
        if (Instance != null) Instance.PlayDoorClose();
    }

    public static void PlayFemalePant()
    {
        if (Instance != null) Instance.PlayFemalePanting();
    }

    public static void PlayMalePant()
    {
        if (Instance != null) Instance.PlayMalePanting();
    }

    public static void PlayInvOpen()
    {
        if (Instance != null) Instance.PlayInventoryOpen();
    }

    public static void PlayInvClose()
    {
        if (Instance != null) Instance.PlayInventoryClose();
    }

    public static void PlayClick()
    {
        if (Instance != null) Instance.PlayButtonClick();
    }

    public static void PlayCraft()
    {
        if (Instance != null) Instance.PlayCrafting();
    }

    public static void PlayDrop()
    {
        if (Instance != null) Instance.PlayDropItem();
    }

    public static void PlayJump(int characterIndex = -1)
    {
        if (Instance != null) Instance.PlayJumping(characterIndex);
    }

    public static void PlayQStart()
    {
        if (Instance != null) Instance.PlayQuestStart();
    }

    public static void PlayQFinish()
    {
        if (Instance != null) Instance.PlayQuestFinish();
    }

    public static float GetQFinishDuration()
    {
        if (Instance != null && Instance.questFinishClip != null)
            return Instance.questFinishClip.length;
        return 2.5f;
    }
}
