using UnityEngine;

/// <summary>
/// Simple global one-shot SFX player. Loads clips from Resources/SFX/ by default.
/// </summary>
public class GameAudio : MonoBehaviour
{
    public static GameAudio Instance { get; private set; }

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
    [SerializeField] private AudioClip questStartClip;
    [SerializeField] private AudioClip questFinishClip;

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
        _source.spatialBlend = 0f; // 2D UI/feedback sounds

        _doorOpenSource = gameObject.AddComponent<AudioSource>();
        _doorOpenSource.playOnAwake = false;
        _doorOpenSource.spatialBlend = 0f;

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
        if (questStartClip == null)
            questStartClip = Resources.Load<AudioClip>("SFX/quest-start");
        if (questFinishClip == null)
            questFinishClip = Resources.Load<AudioClip>("SFX/quest-finish");
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void PlayItemPickup()
    {
        PlayOneShot(itemPickupClip);
    }

    public void PlayDoorKnock()
    {
        PlayOneShot(doorKnockClip);
    }

    public void PlayDoorOpen()
    {
        if (_doorOpenSource != null && doorOpenClip != null)
        {
            _doorOpenSource.clip = doorOpenClip;
            _doorOpenSource.Play();
        }
    }

    public void PlayDoorClose()
    {
        if (_doorOpenSource != null && _doorOpenSource.isPlaying)
        {
            _doorOpenSource.Stop();
        }
        PlayOneShot(doorCloseClip);
    }

    public void PlayFemalePanting()
    {
        PlayOneShot(femalePantingClip);
    }

    public void PlayMalePanting()
    {
        PlayOneShot(malePantingClip);
    }

    public void PlayInventoryOpen()
    {
        PlayOneShot(inventoryOpenClip);
    }

    public void PlayInventoryClose()
    {
        PlayOneShot(inventoryCloseClip);
    }

    public void PlayButtonClick()
    {
        PlayOneShot(buttonClickClip);
    }

    public void PlayCrafting()
    {
        PlayOneShot(craftingClip);
    }

    public void PlayJumping()
    {
        PlayOneShot(jumpingClip);
    }

    public void PlayQuestStart()
    {
        PlayOneShot(questStartClip);
    }

    public void PlayQuestFinish()
    {
        PlayOneShot(questFinishClip);
    }

    public void PlayOneShot(AudioClip clip, float volume = 1f)
    {
        if (clip == null || _source == null) return;
        _source.PlayOneShot(clip, volume);
    }

    public static void PlayPickup()
    {
        if (Instance != null)
            Instance.PlayItemPickup();
    }

    public static void PlayKnock()
    {
        if (Instance != null)
            Instance.PlayDoorKnock();
    }

    public static void PlayOpen()
    {
        if (Instance != null)
            Instance.PlayDoorOpen();
    }

    public static void PlayClose()
    {
        if (Instance != null)
            Instance.PlayDoorClose();
    }

    public static void PlayFemalePant()
    {
        if (Instance != null)
            Instance.PlayFemalePanting();
    }

    public static void PlayMalePant()
    {
        if (Instance != null)
            Instance.PlayMalePanting();
    }

    public static void PlayInvOpen()
    {
        if (Instance != null)
            Instance.PlayInventoryOpen();
    }

    public static void PlayInvClose()
    {
        if (Instance != null)
            Instance.PlayInventoryClose();
    }

    public static void PlayClick()
    {
        if (Instance != null)
            Instance.PlayButtonClick();
    }

    public static void PlayCraft()
    {
        if (Instance != null)
            Instance.PlayCrafting();
    }

    public static void PlayJump()
    {
        if (Instance != null)
            Instance.PlayJumping();
    }

    public static void PlayQStart()
    {
        if (Instance != null)
            Instance.PlayQuestStart();
    }

    public static void PlayQFinish()
    {
        if (Instance != null)
            Instance.PlayQuestFinish();
    }

    public static float GetQFinishDuration()
    {
        if (Instance != null && Instance.questFinishClip != null)
            return Instance.questFinishClip.length;
        return 2.5f;
    }
}
