using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering; // optional, used when driving a Global Volume

[DisallowMultipleComponent]
public class SunlightControl : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Directional light used as the sun. If null the first directional Light found will be used.")]
    public Light sun;

    [Header("Time")]
    [Tooltip("Time of day in hours (0 - 24). 0 = midnight, 12 = noon.")]
    [Range(0f, 24f)]
    public float timeOfDay = 12f;

    [Tooltip("If true the system will advance time automatically (useful for demos).")]
    public bool autoCycle = false;
    [Tooltip("How many in-game hours pass per real second while autoCycle is enabled.")]
    public float hoursPerSecond = 0.1f;

    [Header("Sun appearance")]
    [Tooltip("Sun color over the day. Gradient keys at 0..1 map to 0..24 hours.")]
    public Gradient sunColorOverDay = DefaultSunGradient();
    [Tooltip("Sun intensity multiplier over the day (0..1 on the curve maps to 0..24 hours).")]
    public AnimationCurve sunIntensityOverDay = DefaultIntensityCurve();

    [Header("Moon (night)")]
    [Tooltip("Directional light used as the moon. If null an opposite-directional light will be used if available.")]
    public Light moon;
    [Tooltip("Moon color over the day (mostly used at night).")]
    public Gradient moonColorOverNight = DefaultMoonGradient();
    [Tooltip("Moon intensity curve over normalized day (0..1). Peak at midnight.")]
    public AnimationCurve moonIntensityOverNight = DefaultMoonIntensityCurve();
    [Tooltip("Yaw offset applied to the moon rotation relative to the sun (degrees).")]
    public float moonYawOffset = 10f;
    [Range(0f, 1f)]
    [Tooltip("Cloud cover reduces apparent moon brightness. 0 = clear, 1 = fully clouded.")]
    public float cloudCover = 0f;

    [Header("Fog (global)")]
    public bool enableFog = true;
    public Gradient fogColorOverDay = DefaultFogGradient();
    public AnimationCurve fogDensityOverDay = DefaultFogDensityCurve();
    [Tooltip("Extra multiplier for forest fog when active (use SetForestFogActive).")]
    [Range(0f, 5f)]
    public float forestFogMultiplier = 1.6f;

    [Header("Night fog tuning")]
    [Tooltip("How much stronger the fog becomes at peak-night compared to the base curve. 1 = no extra night fog.")]
    [Min(1f)]
    public float nightFogMultiplier = 2f;
    [Tooltip("Easing applied to night fog progression (higher = smoother)")]
    [Range(0f, 4f)]
    public float nightFogSmoothness = 1f;

    [Header("Behavior")]
    [Tooltip("Normalized threshold (0..1 => 0..24 hours) below which it's considered night. Useful for firing night/day events.")]
    [Range(0f, 1f)]
    public float nightThreshold = 0.25f; // 6:00 -> 0.25

    [Header("Optional URP Volume")]
    [Tooltip("Optional Global Volume (URP) to drive fog/atmosphere at night. Assign a Global Volume with a Fog override.")]
    public Volume globalVolume;

    [Header("Events")]
    public UnityEvent OnDayStart;
    public UnityEvent OnNightStart;
    public UnityEvent<float> OnTimeChanged; // passes timeOfDay

    // internal
    bool lastWasNight = false;
    bool forestFogActive = false;
    Coroutine runningTransition;

    void Reset()
    {
        // sensible defaults
        enableFog = true;
    }

    void Awake()
    {
        if (sun == null)
        {
            // find the first enabled directional light
            var lights = FindObjectsOfType<Light>();
            foreach (var l in lights)
            {
                if (l.type == LightType.Directional)
                {
                    sun = l;
                    break;
                }
            }
        }

        // If no moon assigned try to find a second directional; otherwise leave null and user can assign one.
        if (moon == null)
        {
            var lights = FindObjectsOfType<Light>();
            foreach (var l in lights)
            {
                if (l.type == LightType.Directional && l != sun)
                {
                    moon = l;
                    break;
                }
            }
        }

        if (globalVolume == null)
        {
            var vols = FindObjectsOfType<Volume>();
            foreach (var v in vols)
                if (v.isGlobal) { globalVolume = v; break; }
        }
    }

    void Start()
    {
        // ensure moon is initially configured as weaker / off if needed
        if (moon != null)
        {
            moon.enabled = true; // we control intensity to zero during day to avoid flicker
        }
        if (globalVolume != null) globalVolume.weight = 0f;

        ApplyTimeImmediate(timeOfDay);
    }

    void Update()
    {
        if (autoCycle)
        {
            ShiftTime(Time.deltaTime * hoursPerSecond);
        }
    }

    // PUBLIC API

    // Set absolute time in hours (0..24). If smoothDuration>0 will transition smoothly.
    public void SetTimeOfDay(float hours, float smoothDuration = 0f)
    {
        hours = NormalizeHours(hours);
        if (runningTransition != null) StopCoroutine(runningTransition);
        if (smoothDuration > 0f)
            runningTransition = StartCoroutine(TransitionTimeCoroutine(timeOfDay, hours, smoothDuration));
        else
            ApplyTimeImmediate(hours);
    }

    // Add delta hours to current time
    public void ShiftTime(float deltaHours, float smoothDuration = 0f)
    {
        SetTimeOfDay(timeOfDay + deltaHours, smoothDuration);
    }

    // Directly apply player's progress factor [0..1] to darkness (0 = no effect, 1 = full effect).
    // This multiplies sun intensity (and fog) so you can hook game progress to visual darkness easily.
    public void ApplyProgressDarkness(float progressFactor)
    {
        progressFactor = Mathf.Clamp01(progressFactor);
        // Reduce sun intensity by progress factor (user can tune base curve)
        float baseIntensity = sunIntensityOverDay.Evaluate(timeOfDay / 24f);
        if (sun != null)
            sun.intensity = baseIntensity * (1f - progressFactor);
        // increase fog density
        float baseFog = fogDensityOverDay.Evaluate(timeOfDay / 24f);
        RenderSettings.fogDensity = baseFog * (1f + progressFactor * 2f);
    }

    // Toggle a forest-specific fog boost (call from triggers or your zone system).
    public void SetForestFogActive(bool active)
    {
        forestFogActive = active;
        UpdateFogAndLighting();
    }

    // Set cloud cover [0..1]. Clouds dim the moon.
    public void SetCloudCover(float cover)
    {
        cloudCover = Mathf.Clamp01(cover);
        UpdateFogAndLighting();
    }

    // EVENTS: simple helpers
    // These let external code respond to day/night changes.
    public void RegisterOnDayStart(UnityAction action) => OnDayStart.AddListener(action);
    public void RegisterOnNightStart(UnityAction action) => OnNightStart.AddListener(action);

    // INTERNAL / HELPERS

    static float NormalizeHours(float h)
    {
        h %= 24f;
        if (h < 0f) h += 24f;
        return h;
    }

    IEnumerator TransitionTimeCoroutine(float fromHours, float toHours, float duration)
    {
        float elapsed = 0f;
        // handle wrap around: choose shortest delta
        float delta = Mathf.DeltaAngle(fromHours / 24f * 360f, toHours / 24f * 360f) / 360f * 24f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float current = fromHours + delta * t;
            ApplyTimeImmediate(NormalizeHours(current));
            yield return null;
        }
        ApplyTimeImmediate(toHours);
        runningTransition = null;
    }

    void ApplyTimeImmediate(float hours)
    {
        timeOfDay = NormalizeHours(hours);

        // rotate sun: convert hours (0..24) to inclination (-90..270) so 12:00 = 90deg (overhead)
        // Using: angle = (time/24)*360 - 90
        float sunAngle = (timeOfDay / 24f) * 360f - 90f;
        if (sun != null)
        {
            sun.transform.rotation = Quaternion.Euler(new Vector3(sunAngle, 170f, 0f)); // tweak yaw for direction
            // color & intensity
            Color sunColor = sunColorOverDay.Evaluate(timeOfDay / 24f);
            sun.color = sunColor;
            float intensity = sunIntensityOverDay.Evaluate(timeOfDay / 24f);
            sun.intensity = intensity;
        }

        // Moon: place roughly opposite the sun and set low-intensity glow at night, reduced by clouds
        ApplyMoon(timeOfDay);

        UpdateFogAndLighting();

        // Fire time-changed event
        OnTimeChanged?.Invoke(timeOfDay);

        // handle day/night events
        bool isNight = (timeOfDay / 24f) < nightThreshold || (timeOfDay / 24f) > (1f - nightThreshold);
        if (isNight && !lastWasNight)
        {
            lastWasNight = true;
            OnNightStart?.Invoke();
        }
        else if (!isNight && lastWasNight)
        {
            lastWasNight = false;
            OnDayStart?.Invoke();
        }
    }

    void ApplyMoon(float hours)
    {
        if (moon == null) return;

        float n = hours / 24f; // 0..1
        // Moon should be strongest during night. Compute night factor (1 at midnight, 0 during day)
        float nightFactor = 1f - Mathf.Clamp01(Mathf.InverseLerp(nightThreshold, 1f - nightThreshold, n));

        // Position moon opposite the sun
        float moonAngle = (hours / 24f) * 360f - 90f + 180f; // opposite hemisphere
        moon.transform.rotation = Quaternion.Euler(new Vector3(moonAngle, 170f + moonYawOffset, 0f));

        // Color & base intensity
        Color moonCol = moonColorOverNight.Evaluate(n);
        float baseMoonIntensity = moonIntensityOverNight.Evaluate(n);

        // combine with nightFactor so moon is essentially zero during day
        float intensity = baseMoonIntensity * nightFactor;

        // clouds dim the moon (cloudCover 0..1). Make clouds significantly reduce intensity but not fully zero.
        intensity *= Mathf.Lerp(1f, 0.25f, cloudCover);

        moon.color = moonCol;
        moon.intensity = intensity;

        // Optionally enable/disable light to avoid expensive calculations when off
        moon.enabled = intensity > 0.001f;
    }

    // NEW: compute nightProgress 0..1 that rises from 0 at night-start to 1 at midnight, then falls to 0 at night-end.
    float ComputeNightProgress(float normalizedHour)
    {
        float nt = Mathf.Clamp01(nightThreshold);
        if (nt <= 0f) return 0f;

        // late-night segment (before midnight)
        if (normalizedHour >= 1f - nt)
        {
            return Mathf.InverseLerp(1f - nt, 1f, normalizedHour);
        }

        // early-night segment (after midnight)
        if (normalizedHour <= nt)
        {
            return Mathf.InverseLerp(nt, 0f, normalizedHour); // maps 0..nt -> 1..0, so invert below
        }

        return 0f;
    }

    void UpdateFogAndLighting()
    {
        float n = timeOfDay / 24f;
        // night progress increases from 0 at night-start to 1 at midnight
        float rawNightProgress = ComputeNightProgress(n);
        // apply smoothing curve
        float nightProgress = Mathf.Pow(Mathf.SmoothStep(0f, 1f, rawNightProgress), Mathf.Max(0.0001f, nightFogSmoothness));

        // base fog from curve
        float baseDensity = fogDensityOverDay.Evaluate(n);
        // apply night multiplier gradually as night progresses
        float density = baseDensity * Mathf.Lerp(1f, nightFogMultiplier, nightProgress);
        // apply forest multiplier
        density *= (forestFogActive ? forestFogMultiplier : 1f);

        // build fog color so it doesn't go fully black — blend base gradient with a night tint by nightProgress
        Color dayFog = fogColorOverDay.Evaluate(n);
        Color nightTint = new Color(0.08f, 0.09f, 0.12f); // subtle blue-gray at night
        Color finalFogCol = Color.Lerp(dayFog, nightTint, nightProgress * 0.8f);

        // Built-in fallback
        if (enableFog)
        {
            RenderSettings.fog = true;
            RenderSettings.fogColor = finalFogCol;
            RenderSettings.fogDensity = density;
        }
        else
        {
            RenderSettings.fog = false;
        }

        // URP/Volume optional: drive volume weight by nightProgress (clamped)
        if (globalVolume != null)
        {
            float w = Mathf.Clamp01(nightProgress * (forestFogActive ? forestFogMultiplier : 1f));
            globalVolume.weight = w;
        }

        // ambient lighting
        float ambientScale = Mathf.Clamp01(sunIntensityOverDay.Evaluate(n));
        RenderSettings.ambientIntensity = Mathf.Lerp(0.2f, 1f, ambientScale);
    }

    void ApplyTimeImmediateNormalized(float normalized) => ApplyTimeImmediate(normalized * 24f);

    // Optional convenience: smooth transition helper
    public void TransitionToNight(float duration) => SetTimeOfDay(22f, duration);

    // Optional convenience: smooth transition helper
    public void TransitionToDay(float duration) => SetTimeOfDay(10f, duration);

    // Debug / Editor helper
#if UNITY_EDITOR
    void OnValidate()
    {
        if (!Application.isPlaying)
        {
            // don't run heavy calls in edit-time except simple defaults
            if (sun == null)
            {
                var l = FindObjectOfType<Light>();
                if (l != null && l.type == LightType.Directional) sun = l;
            }

            if (moon == null)
            {
                var lights = FindObjectsOfType<Light>();
                foreach (var ll in lights)
                {
                    if (ll.type == LightType.Directional && ll != sun)
                    {
                        moon = ll;
                        break;
                    }
                }
            }
        }
    }
#endif

    // Default gradient/curves
    static Gradient DefaultSunGradient()
    {
        Gradient g = new Gradient();
        g.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(new Color(0.05f,0.05f,0.12f), 0f),     // midnight
                new GradientColorKey(new Color(1f,0.5f,0.2f), 0.2f),       // sunrise
                new GradientColorKey(new Color(1f,1f,0.95f), 0.5f),       // noon
                new GradientColorKey(new Color(1f,0.5f,0.2f), 0.8f),      // sunset
                new GradientColorKey(new Color(0.05f,0.05f,0.12f), 1f)    // midnight
            },
            new GradientAlphaKey[] { new GradientAlphaKey(1f,0f), new GradientAlphaKey(1f,1f) }
        );
        return g;
    }

    static AnimationCurve DefaultIntensityCurve()
    {
        // normalized 0..1 -> intensity multiplier
        return new AnimationCurve(new Keyframe(0f, 0.05f), new Keyframe(0.2f, 0.6f), new Keyframe(0.5f, 1f), new Keyframe(0.8f, 0.6f), new Keyframe(1f, 0.05f));
    }

    static Gradient DefaultFogGradient()
    {
        Gradient g = new Gradient();
        g.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(new Color(0.07f,0.08f,0.09f), 0f),
                new GradientColorKey(new Color(0.6f,0.6f,0.65f), 0.3f),
                new GradientColorKey(new Color(0.6f,0.6f,0.65f), 0.7f),
                new GradientColorKey(new Color(0.07f,0.08f,0.09f), 1f)
            },
            new GradientAlphaKey[] { new GradientAlphaKey(1f,0f), new GradientAlphaKey(1f,1f) }
        );
        return g;
    }

    static AnimationCurve DefaultFogDensityCurve()
    {
        // low during day, higher at dawn/dusk and night
        return new AnimationCurve(new Keyframe(0f, 0.002f), new Keyframe(0.2f, 0.01f), new Keyframe(0.5f, 0.0015f), new Keyframe(0.8f, 0.01f), new Keyframe(1f, 0.002f));
    }

    static Gradient DefaultMoonGradient()
    {
        Gradient g = new Gradient();
        g.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(new Color(0.6f,0.65f,0.8f), 0f), // midnight (cool bluish)
                new GradientColorKey(new Color(0.6f,0.65f,0.8f), 1f)
            },
            new GradientAlphaKey[] { new GradientAlphaKey(1f,0f), new GradientAlphaKey(1f,1f) }
        );
        return g;
    }

    static AnimationCurve DefaultMoonIntensityCurve()
    {
        // moon is effectively zero in day, peaks at night (normalized)
        return new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.25f, 0.2f), new Keyframe(0.5f, 0.35f), new Keyframe(0.75f, 0.2f), new Keyframe(1f, 0f));
    }
}
