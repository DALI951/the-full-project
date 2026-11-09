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

    [Header("Additional SFX")]
    [SerializeField] private AudioClip buildPlace;
    [SerializeField] private AudioClip swordHit;
    [SerializeField] private AudioClip unitTrain;
    [SerializeField] private AudioClip chop;
    [SerializeField] private AudioClip mine;

    [Header("AoE4 Combat & Fanfare SFX")]
    [SerializeField] private AudioClip ageUpFanfare;
    [SerializeField] private AudioClip techResearch;
    [SerializeField] private AudioClip marketTrade;
    [SerializeField] private AudioClip bowRelease;
    [SerializeField] private AudioClip arrowHit;

    private bool isInitialized = false;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        EnsureAudioSources();
        InitializeClips();
    }

    private void Start()
    {
        EnsureAudioSources();
        isInitialized = true;
    }

    private void EnsureAudioSources()
    {
        if (musicSource == null)
        {
            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.loop = true;
            musicSource.volume = 0.5f;
        }

        if (sfxSource == null)
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.loop = false;
        }
    }

    /// <summary>
    /// Generates short, clean PCM wave AudioClips procedurally in memory.
    /// </summary>
    public static AudioClip CreateToneClip(string name, float startFreq, float endFreq, float duration, float volume = 0.5f, bool noise = false)
    {
        const int sampleRate = 44100;
        int sampleCount = Mathf.Max(1, (int)(sampleRate * duration));
        float[] samples = new float[sampleCount];

        float attackTime = Mathf.Min(0.01f, duration * 0.1f);
        float releaseTime = Mathf.Min(0.05f, duration * 0.3f);

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleRate;
            float progress = (float)i / sampleCount;

            // Instantaneous phase for linear frequency chirp
            float phase = 2f * Mathf.PI * (startFreq * t + (endFreq - startFreq) * 0.5f * (t * progress));
            float wave = Mathf.Sin(phase);

            if (noise)
            {
                float whiteNoise = (UnityEngine.Random.value * 2f - 1f);
                wave = Mathf.Lerp(wave, whiteNoise, 0.45f);
            }

            // Smooth attack and release envelope to prevent clicking
            float envelope = 1f;
            if (t < attackTime)
                envelope = t / attackTime;
            else if (t > duration - releaseTime)
                envelope = Mathf.Max(0f, (duration - t) / releaseTime);

            samples[i] = wave * volume * envelope;
        }

        AudioClip clip = AudioClip.Create(name, sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private AudioClip GetOrCreateClip(ref AudioClip clip, string resourceName, float startFreq, float endFreq, float duration, float volume = 0.5f, bool noise = false)
    {
        if (clip != null) return clip;
        clip = Resources.Load<AudioClip>("Audio/" + resourceName);
        if (clip == null)
            clip = Resources.Load<AudioClip>(resourceName);
        if (clip == null)
            clip = CreateToneClip(resourceName, startFreq, endFreq, duration, volume, noise);
        return clip;
    }

    private AudioClip GetOrCreateClip(ref AudioClip clip, string resourceName, System.Func<AudioClip> factory)
    {
        if (clip != null) return clip;
        clip = Resources.Load<AudioClip>("Audio/" + resourceName);
        if (clip == null)
            clip = Resources.Load<AudioClip>(resourceName);
        if (clip == null && factory != null)
            clip = factory();
        return clip;
    }

    /// <summary>
    /// AoE4 Majestic Medieval Brass Horn Fanfare Chord:
    /// Herald trumpet intro resolving into a noble, resonant C-major brass chord.
    /// </summary>
    public static AudioClip CreateAgeUpFanfareClip()
    {
        const int sampleRate = 44100;
        const float duration = 2.2f;
        int sampleCount = (int)(sampleRate * duration);
        float[] samples = new float[sampleCount];

        float[] chordFreqs = new float[] { 261.63f, 329.63f, 392.00f, 523.25f }; // C4, E4, G4, C5

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleRate;
            float sample = 0f;

            if (t < 0.22f)
            {
                // Herald intro note: G4
                float env = Mathf.Min(t / 0.02f, 1f) * Mathf.Clamp01((0.22f - t) / 0.04f);
                sample = GenerateBrassTone(392.00f, t) * env * 0.65f;
            }
            else if (t < 0.44f)
            {
                // Herald intro note: C5
                float localT = t - 0.22f;
                float env = Mathf.Min(localT / 0.02f, 1f) * Mathf.Clamp01((0.22f - localT) / 0.04f);
                sample = GenerateBrassTone(523.25f, t) * env * 0.7f;
            }
            else
            {
                // Grand Major Brass Chord
                float chordT = t - 0.44f;
                float attack = Mathf.Min(chordT / 0.06f, 1f);
                float decay = Mathf.Clamp01((1.76f - chordT) / 0.6f);
                float env = attack * decay;

                // Subtle slow vibrato for symphonic warmth
                float vib = 1f + 0.005f * Mathf.Sin(2f * Mathf.PI * 4.5f * chordT);

                float chordMix = 0f;
                for (int c = 0; c < chordFreqs.Length; c++)
                {
                    chordMix += GenerateBrassTone(chordFreqs[c] * vib, t);
                }
                chordMix /= chordFreqs.Length;
                sample = chordMix * env * 0.78f;
            }

            samples[i] = Mathf.Clamp(sample * 1.05f, -0.95f, 0.95f);
        }

        AudioClip clip = AudioClip.Create("AgeUpFanfare", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private static float GenerateBrassTone(float freq, float t)
    {
        float p1 = 2f * Mathf.PI * freq * t;
        float wave = Mathf.Sin(p1)
                   + 0.55f * Mathf.Sin(2f * p1)
                   + 0.35f * Mathf.Sin(3f * p1)
                   + 0.20f * Mathf.Sin(4f * p1)
                   + 0.10f * Mathf.Sin(5f * p1);
        return wave * 0.45f;
    }

    /// <summary>
    /// AoE4 Tech Research: Blacksmith hammer strike on steel anvil + ascending golden discovery chime.
    /// </summary>
    public static AudioClip CreateTechResearchClip()
    {
        const int sampleRate = 44100;
        const float duration = 1.15f;
        int sampleCount = (int)(sampleRate * duration);
        float[] samples = new float[sampleCount];

        float[] chimeNotes = new float[] { 1318.5f, 1661.2f, 1975.5f, 2637.0f }; // E6, G#6, B6, E7
        float[] chimeDelays = new float[] { 0.08f, 0.18f, 0.28f, 0.38f };

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleRate;
            float sample = 0f;

            // 1. Anvil strike impact & body resonance
            if (t < 0.9f)
            {
                float transient = 0f;
                if (t < 0.025f)
                {
                    float noise = (UnityEngine.Random.value * 2f - 1f) * 0.5f;
                    transient = (Mathf.Sin(2f * Mathf.PI * 1450f * t) + noise) * Mathf.Exp(-t * 120f);
                }

                float ring1 = Mathf.Sin(2f * Mathf.PI * 1780f * t);
                float ring2 = Mathf.Sin(2f * Mathf.PI * 2920f * t) * 0.6f;
                float decay = Mathf.Exp(-t * 5.2f);
                sample += (transient * 0.5f + (ring1 + ring2) * 0.4f * decay);
            }

            // 2. Ascending crystalline research chimes
            for (int c = 0; c < chimeNotes.Length; c++)
            {
                float ct = t - chimeDelays[c];
                if (ct > 0f)
                {
                    float chimeEnv = Mathf.Min(ct / 0.005f, 1f) * Mathf.Exp(-ct * 6.0f);
                    float chimeWave = Mathf.Sin(2f * Mathf.PI * chimeNotes[c] * ct)
                                    + 0.25f * Mathf.Sin(4f * Mathf.PI * chimeNotes[c] * ct);
                    sample += chimeWave * chimeEnv * 0.32f;
                }
            }

            samples[i] = Mathf.Clamp(sample * 0.72f, -0.95f, 0.95f);
        }

        AudioClip clip = AudioClip.Create("TechResearch", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>
    /// AoE4 Market Trade: Crisp gold coins clinking and settling into a treasury purse.
    /// </summary>
    public static AudioClip CreateMarketTradeClip()
    {
        const int sampleRate = 44100;
        const float duration = 0.38f;
        int sampleCount = (int)(sampleRate * duration);
        float[] samples = new float[sampleCount];

        float[] coinTimes = new float[] { 0.00f, 0.055f, 0.12f };
        float[] coinPitches = new float[] { 3400f, 3850f, 4400f };

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleRate;
            float sample = 0f;

            for (int c = 0; c < coinTimes.Length; c++)
            {
                float ct = t - coinTimes[c];
                if (ct > 0f && ct < 0.22f)
                {
                    float attack = Mathf.Min(ct / 0.001f, 1f);
                    float decay = Mathf.Exp(-ct * 28f);
                    float env = attack * decay;

                    float p1 = 2f * Mathf.PI * coinPitches[c] * ct;
                    float wave = Mathf.Sin(p1)
                               + 0.45f * Mathf.Sin(p1 * 1.48f)
                               + 0.2f * Mathf.Sin(p1 * 2.1f);

                    sample += wave * env * 0.42f;
                }
            }

            samples[i] = Mathf.Clamp(sample, -0.95f, 0.95f);
        }

        AudioClip clip = AudioClip.Create("MarketTrade", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>
    /// AoE4 Bow Release: Crisp bowstring snap twang + feathered arrow whoosh for watchtowers.
    /// </summary>
    public static AudioClip CreateBowReleaseClip()
    {
        const int sampleRate = 44100;
        const float duration = 0.28f;
        int sampleCount = (int)(sampleRate * duration);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleRate;

            // Bowstring snap
            float twang = 0f;
            if (t < 0.14f)
            {
                float pitch = Mathf.Lerp(420f, 140f, t / 0.06f);
                float phase = 2f * Mathf.PI * pitch * t;
                float stringWave = Mathf.Sin(phase) + 0.4f * Mathf.Sin(2f * phase);
                float attack = Mathf.Min(t / 0.002f, 1f);
                float decay = Mathf.Exp(-t * 22f);
                twang = stringWave * attack * decay * 0.65f;
            }

            // Arrow whoosh through air
            float whoosh = 0f;
            if (t > 0.015f && t < 0.24f)
            {
                float wt = t - 0.015f;
                float env = Mathf.Sin((wt / 0.225f) * Mathf.PI);
                float noise = (UnityEngine.Random.value * 2f - 1f);
                float whooshTone = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(850f, 1300f, wt / 0.22f) * t);
                whoosh = (noise * 0.4f + whooshTone * 0.6f) * env * 0.35f;
            }

            samples[i] = Mathf.Clamp(twang + whoosh, -0.95f, 0.95f);
        }

        AudioClip clip = AudioClip.Create("BowRelease", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>
    /// AoE4 Arrow Impact: Crisp impact tick + solid wood/armor thud.
    /// </summary>
    public static AudioClip CreateArrowHitClip()
    {
        const int sampleRate = 44100;
        const float duration = 0.18f;
        int sampleCount = (int)(sampleRate * duration);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleRate;

            float tick = 0f;
            if (t < 0.01f)
            {
                tick = (UnityEngine.Random.value * 2f - 1f) * (1f - t / 0.01f) * 0.5f;
            }

            float pitch = Mathf.Lerp(220f, 60f, Mathf.Clamp01(t / 0.08f));
            float phase = 2f * Mathf.PI * pitch * t;
            float thudWave = Mathf.Sin(phase) + 0.3f * Mathf.Sin(2f * phase);
            float decay = Mathf.Exp(-t * 24f);
            float thud = thudWave * decay * 0.7f;

            samples[i] = Mathf.Clamp(tick + thud, -0.95f, 0.95f);
        }

        AudioClip clip = AudioClip.Create("ArrowHit", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private void InitializeClips()
    {
        GetOrCreateClip(ref menuMusic, "MenuMusic", 220f, 330f, 2.0f, 0.25f, false);
        GetOrCreateClip(ref gameMusic, "GameMusic", 260f, 390f, 2.5f, 0.25f, false);
        GetOrCreateClip(ref battleMusic, "BattleMusic", 180f, 240f, 2.0f, 0.3f, false);
        GetOrCreateClip(ref victoryMusic, "VictoryMusic", 440f, 880f, 1.2f, 0.5f, false);
        GetOrCreateClip(ref defeatMusic, "DefeatMusic", 300f, 100f, 1.5f, 0.5f, false);

        GetOrCreateClip(ref unitSelect, "UnitSelect", 440f, 660f, 0.08f, 0.35f, false);
        GetOrCreateClip(ref unitMove, "UnitMove", 330f, 495f, 0.1f, 0.35f, false);
        GetOrCreateClip(ref unitAttack, "UnitAttack", 520f, 260f, 0.12f, 0.4f, false);
        GetOrCreateClip(ref unitDeath, "UnitDeath", 350f, 100f, 0.35f, 0.4f, true);
        GetOrCreateClip(ref buildingComplete, "BuildingComplete", 523f, 1046f, 0.3f, 0.4f, false);
        GetOrCreateClip(ref buildingDestroyed, "BuildingDestroyed", 120f, 40f, 0.5f, 0.6f, true);
        GetOrCreateClip(ref resourceGather, "ResourceGather", 600f, 900f, 0.08f, 0.3f, false);
        GetOrCreateClip(ref uiClick, "UIClick", 800f, 400f, 0.05f, 0.4f, false);
        GetOrCreateClip(ref uiError, "UIError", 150f, 120f, 0.25f, 0.4f, true);
        GetOrCreateClip(ref notification, "Notification", 587f, 880f, 0.18f, 0.4f, false);
        GetOrCreateClip(ref waveHorn, "WaveHorn", 130f, 110f, 0.8f, 0.6f, false);

        GetOrCreateClip(ref buildPlace, "BuildPlace", 200f, 80f, 0.12f, 0.5f, true);
        GetOrCreateClip(ref swordHit, "SwordHit", 900f, 200f, 0.15f, 0.5f, true);
        GetOrCreateClip(ref unitTrain, "UnitTrain", 440f, 880f, 0.2f, 0.4f, false);
        GetOrCreateClip(ref chop, "Chop", 400f, 150f, 0.1f, 0.45f, true);
        GetOrCreateClip(ref mine, "Mine", 800f, 350f, 0.09f, 0.45f, true);

        // AoE4 Cues
        GetOrCreateClip(ref ageUpFanfare, "AgeUpFanfare", CreateAgeUpFanfareClip);
        GetOrCreateClip(ref techResearch, "TechResearch", CreateTechResearchClip);
        GetOrCreateClip(ref marketTrade, "MarketTrade", CreateMarketTradeClip);
        GetOrCreateClip(ref bowRelease, "BowRelease", CreateBowReleaseClip);
        GetOrCreateClip(ref arrowHit, "ArrowHit", CreateArrowHitClip);
    }

    public void PlayMenuMusic() => PlayMusic(menuMusic != null ? menuMusic : GetOrCreateClip(ref menuMusic, "MenuMusic", 220f, 330f, 2.0f, 0.25f, false));
    public void PlayGameMusic() => PlayMusic(gameMusic != null ? gameMusic : GetOrCreateClip(ref gameMusic, "GameMusic", 260f, 390f, 2.5f, 0.25f, false));
    public void PlayBattleMusic()
    {
        AudioClip clip = battleMusic != null ? battleMusic : GetOrCreateClip(ref battleMusic, "BattleMusic", 180f, 240f, 2.0f, 0.3f, false);
        if (musicSource == null || musicSource.clip != clip || !musicSource.isPlaying)
            PlayMusic(clip);
    }
    public void PlayVictoryMusic() => PlayMusic(victoryMusic != null ? victoryMusic : GetOrCreateClip(ref victoryMusic, "VictoryMusic", 440f, 880f, 1.2f, 0.5f, false));
    public void PlayDefeatMusic() => PlayMusic(defeatMusic != null ? defeatMusic : GetOrCreateClip(ref defeatMusic, "DefeatMusic", 300f, 100f, 1.5f, 0.5f, false));

    public void PlayUnitSelect() => PlaySFX(unitSelect != null ? unitSelect : GetOrCreateClip(ref unitSelect, "UnitSelect", 440f, 660f, 0.08f, 0.35f, false));
    public void PlayUnitMove() => PlaySFX(unitMove != null ? unitMove : GetOrCreateClip(ref unitMove, "UnitMove", 330f, 495f, 0.1f, 0.35f, false));
    public void PlayUnitAttack() => PlaySFX(unitAttack != null ? unitAttack : GetOrCreateClip(ref unitAttack, "UnitAttack", 520f, 260f, 0.12f, 0.4f, false));
    public void PlayUnitDeath() => PlaySFX(unitDeath != null ? unitDeath : GetOrCreateClip(ref unitDeath, "UnitDeath", 350f, 100f, 0.35f, 0.4f, true));
    public void PlayBuildingComplete() => PlaySFX(buildingComplete != null ? buildingComplete : GetOrCreateClip(ref buildingComplete, "BuildingComplete", 523f, 1046f, 0.3f, 0.4f, false));
    public void PlayBuildingDestroyed() => PlaySFX(buildingDestroyed != null ? buildingDestroyed : GetOrCreateClip(ref buildingDestroyed, "BuildingDestroyed", 120f, 40f, 0.5f, 0.6f, true));
    public void PlayResourceGather() => PlaySFX(resourceGather != null ? resourceGather : GetOrCreateClip(ref resourceGather, "ResourceGather", 600f, 900f, 0.08f, 0.3f, false));
    public void PlayUIClick() => PlaySFX(uiClick != null ? uiClick : GetOrCreateClip(ref uiClick, "UIClick", 800f, 400f, 0.05f, 0.4f, false));
    public void PlayUIError() => PlaySFX(uiError != null ? uiError : GetOrCreateClip(ref uiError, "UIError", 150f, 120f, 0.25f, 0.4f, true));
    public void PlayNotification() => PlaySFX(notification != null ? notification : GetOrCreateClip(ref notification, "Notification", 587f, 880f, 0.18f, 0.4f, false));
    public void PlayWaveHorn() => PlaySFX(waveHorn != null ? waveHorn : GetOrCreateClip(ref waveHorn, "WaveHorn", 130f, 110f, 0.8f, 0.6f, false));

    // Aliases and additional methods required by game callers
    public void PlayButtonClick() => PlayUIClick();
    public void PlayBuildPlace() => PlaySFX(buildPlace != null ? buildPlace : GetOrCreateClip(ref buildPlace, "BuildPlace", 200f, 80f, 0.12f, 0.5f, true));
    public void PlayBuildDone() => PlayBuildingComplete();
    public void PlaySwordHit() => PlaySFX(swordHit != null ? swordHit : GetOrCreateClip(ref swordHit, "SwordHit", 900f, 200f, 0.15f, 0.5f, true));
    public void PlayUnitTrain() => PlaySFX(unitTrain != null ? unitTrain : GetOrCreateClip(ref unitTrain, "UnitTrain", 440f, 880f, 0.2f, 0.4f, false));
    public void PlayChop() => PlaySFX(chop != null ? chop : GetOrCreateClip(ref chop, "Chop", 400f, 150f, 0.1f, 0.45f, true));
    public void PlayMine() => PlaySFX(mine != null ? mine : GetOrCreateClip(ref mine, "Mine", 800f, 350f, 0.09f, 0.45f, true));
    public void PlayGather() => PlayResourceGather();
    public void PlayVictory() => PlayVictoryMusic();
    public void PlayDefeat() => PlayDefeatMusic();

    // AoE4 Cues Public API
    public void PlayAgeUpFanfare() => PlaySFX(ageUpFanfare != null ? ageUpFanfare : GetOrCreateClip(ref ageUpFanfare, "AgeUpFanfare", CreateAgeUpFanfareClip));
    public void PlayTechResearch() => PlaySFX(techResearch != null ? techResearch : GetOrCreateClip(ref techResearch, "TechResearch", CreateTechResearchClip));
    public void PlayMarketTrade() => PlaySFX(marketTrade != null ? marketTrade : GetOrCreateClip(ref marketTrade, "MarketTrade", CreateMarketTradeClip));
    public void PlayBowRelease() => PlaySFX(bowRelease != null ? bowRelease : GetOrCreateClip(ref bowRelease, "BowRelease", CreateBowReleaseClip));
    public void PlayArrowHit() => PlaySFX(arrowHit != null ? arrowHit : GetOrCreateClip(ref arrowHit, "ArrowHit", CreateArrowHitClip));

    private void PlayMusic(AudioClip clip)
    {
        EnsureAudioSources();
        if (musicSource == null || clip == null) return;
        musicSource.clip = clip;
        musicSource.Play();
    }

    private void PlaySFX(AudioClip clip)
    {
        EnsureAudioSources();
        if (sfxSource == null || clip == null) return;
        sfxSource.PlayOneShot(clip);
    }

    public void SetMusicVolume(float vol) { if (musicSource != null) musicSource.volume = Mathf.Clamp01(vol); }
    public void SetSFXVolume(float vol) { if (sfxSource != null) sfxSource.volume = Mathf.Clamp01(vol); }

    public bool IsInitialized => isInitialized;

    /// <summary>
    /// Clears the static Instance when this object dies.
    /// A destroyed MonoBehaviour is NOT a real CLR null (Unity fake-null only affects
    /// == and !=). Any "Instance?.Foo()" call site elsewhere would therefore still
    /// dispatch into this dead object and throw MissingReferenceException the next
    /// time the scene reloads. Clearing the field makes it a true null.
    /// </summary>
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
