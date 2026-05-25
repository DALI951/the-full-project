using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Music")]
    [SerializeField] private AudioClip menuMusic;
    [SerializeField] private AudioClip gameMusic;
    [SerializeField] private AudioClip battleMusic;
    [SerializeField] private AudioClip victoryMusic;
    [SerializeField] private AudioClip defeatMusic;
    private AudioSource musicSource;

    [Header("SFX")]
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioClip unitSelect;
    [SerializeField] private AudioClip unitMove;
    [SerializeField] private AudioClip unitAttack;
    [SerializeField] private AudioClip unitDeath;
    [SerializeField] private AudioClip buildingComplete;
    [SerializeField] private AudioClip buildingDestroyed;
    [SerializeField] private AudioClip resourceGather;
    [SerializeField] private AudioClip uiClick;
    [SerializeField] private AudioClip uiError;
    [SerializeField] private AudioClip notification;
    [SerializeField] private AudioClip waveHorn;

    private bool isInitialized = false;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.loop = true;
        musicSource.volume = 0.5f;

        if (sfxSource == null)
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.loop = false;
        }

        isInitialized = true;
    }

    public void PlayMenuMusic()
    {
        if (menuMusic != null) PlayMusic(menuMusic);
    }

    public void PlayGameMusic()
    {
        if (gameMusic != null) PlayMusic(gameMusic);
    }

    public void PlayBattleMusic()
    {
        if (battleMusic != null && (musicSource.clip != battleMusic || !musicSource.isPlaying))
            PlayMusic(battleMusic);
    }

    public void PlayVictoryMusic()
    {
        if (victoryMusic != null) PlayMusic(victoryMusic);
    }

    public void PlayDefeatMusic()
    {
        if (defeatMusic != null) PlayMusic(defeatMusic);
    }

    public void PlayUnitSelect() => PlaySFX(unitSelect);
    public void PlayUnitMove() => PlaySFX(unitMove);
    public void PlayUnitAttack() => PlaySFX(unitAttack);
    public void PlayUnitDeath() => PlaySFX(unitDeath);
    public void PlayBuildingComplete() => PlaySFX(buildingComplete);
    public void PlayBuildingDestroyed() => PlaySFX(buildingDestroyed);
    public void PlayResourceGather() => PlaySFX(resourceGather);
    public void PlayUIClick() => PlaySFX(uiClick);
    public void PlayUIError() => PlaySFX(uiError);
    public void PlayNotification() => PlaySFX(notification);
    public void PlayWaveHorn() => PlaySFX(waveHorn);

    private void PlayMusic(AudioClip clip)
    {
        if (musicSource == null || clip == null) return;
        musicSource.clip = clip;
        musicSource.Play();
    }

    private void PlaySFX(AudioClip clip)
    {
        if (sfxSource == null || clip == null) return;
        sfxSource.PlayOneShot(clip);
    }

    public void SetMusicVolume(float vol) { if (musicSource != null) musicSource.volume = Mathf.Clamp01(vol); }
    public void SetSFXVolume(float vol) { if (sfxSource != null) sfxSource.volume = Mathf.Clamp01(vol); }

    public bool IsInitialized => isInitialized;
}