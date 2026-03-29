using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class SunlightControl : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Directional light used as the sun. If null the first directional Light found will be used.")]
    public Light sun;

    [Tooltip("Directional light used as the moon. If null a second directional light in the scene will be used.")]
    public Light moon;

    [Header("Time")]
    [Tooltip("Time of day in hours (0 - 24). 0 = midnight, 12 = noon.")]
    [Range(0f, 24f)]
    public float timeOfDay = 12f;

    [Tooltip("If true the system will advance time automatically (useful for demos).")]
    public bool autoCycle = false;
    [Tooltip("How many in-game hours pass per real second while autoCycle is enabled.")]
    public float hoursPerSecond = 0.1f;

    [Header("Sun appearance")]
    public Gradient sunColorOverDay = DefaultSunGradient();
    public AnimationCurve sunIntensityOverDay = DefaultIntensityCurve();

    [Header("Moon (night)")]
    public Gradient moonColorOverNight = DefaultMoonGradient();
    public AnimationCurve moonIntensityOverNight = DefaultMoonIntensityCurve();
    public float moonYawOffset = 10f;

    [Header("Fog (global)")]
    public bool enableFog = true;
    public Gradient fogColorOverDay = DefaultFogGradient();
    public AnimationCurve fogDensityOverDay = DefaultFogDensityCurve();

    [Header("Night fog tuning")]
    [Min(1f)]
    public float nightFogMultiplier = 2f;
    [Range(0f, 4f)]
    public float nightFogSmoothness = 1f;

    [Header("Skybox tuning")]
    [Tooltip("Sun scale value below which the day skybox starts fading out.")]
    [Range(0.05f, 0.5f)]
    public float skyboxDayThreshold = 0.2f;
    [Tooltip("Sun scale value below which the night skybox is fully applied.")]
    [Range(0.01f, 0.2f)]
    public float skyboxNightThreshold = 0.05f;
    [Tooltip("Minimum skybox exposure at night (gives a faint moonlit glow, 0 = pure black).")]
    [Range(0f, 0.15f)]
    public float nightSkyExposure = 0.04f;
    [Tooltip("Minimum atmospheric thickness at night.")]
    [Range(0f, 0.3f)]
    public float nightAtmosphericThickness = 0.05f;

    [Header("Behavior")]
    [Range(0f, 1f)]
    [Tooltip("Normalized threshold (0..1 => 0..24 hours) below which it's considered night.")]
    public float nightThreshold = 0.25f; // 6:00 -> 0.25

    // Internal
    Coroutine _runningTransition;
    bool _sunIsActive = true;
    float _lastSkyExposure = -1f;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    void Awake()
    {
        // Single pass: find sun (first directional) and moon (second directional).
        if (sun == null || moon == null)
        {
            var lights = FindObjectsOfType<Light>();
            foreach (var l in lights)
            {
                if (l.type != LightType.Directional) continue;
                if (sun == null) { sun = l; continue; }
                if (moon == null) { moon = l; break; }
            }
        }
    }

    void Start()
    {
        if (moon != null)
            moon.enabled = true; // intensity controlled to zero during day

        ApplyTimeImmediate(timeOfDay);
    }

    void Update()
    {
        if (autoCycle)
            ShiftTime(Time.deltaTime * hoursPerSecond);
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>Set absolute time in hours (0..24). Pass smoothDuration > 0 for a smooth transition.</summary>
    public void SetTimeOfDay(float hours, float smoothDuration = 0f)
    {
        hours = NormalizeHours(hours);
        if (_runningTransition != null) StopCoroutine(_runningTransition);
        if (smoothDuration > 0f)
            _runningTransition = StartCoroutine(TransitionTimeCoroutine(timeOfDay, hours, smoothDuration));
        else
            ApplyTimeImmediate(hours);
    }

    /// <summary>Add delta hours to the current time.</summary>
    public void ShiftTime(float deltaHours, float smoothDuration = 0f)
    {
        SetTimeOfDay(timeOfDay + deltaHours, smoothDuration);
    }

    // -------------------------------------------------------------------------
    // Internal helpers
    // -------------------------------------------------------------------------

    static float NormalizeHours(float h)
    {
        h %= 24f;
        if (h < 0f) h += 24f;
        return h;
    }

    IEnumerator TransitionTimeCoroutine(float fromHours, float toHours, float duration)
    {
        float elapsed = 0f;
        // Shortest delta via angle wrap
        float delta = Mathf.DeltaAngle(fromHours / 24f * 360f, toHours / 24f * 360f) / 360f * 24f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            ApplyTimeImmediate(NormalizeHours(fromHours + delta * t));
            yield return null;
        }
        ApplyTimeImmediate(toHours);
        _runningTransition = null;
    }

    void ApplyTimeImmediate(float hours)
    {
        timeOfDay = NormalizeHours(hours);

        float n = timeOfDay / 24f;

        // --- Sun ---
        // angle: 0h = -90 (below horizon), 12h = +90 (overhead)
        float sunAngle = n * 360f - 90f;
        if (sun != null)
        {
            sun.transform.rotation = Quaternion.Euler(sunAngle, 45f, 0f);
            sun.color = sunColorOverDay.Evaluate(n);
            sun.intensity = sunIntensityOverDay.Evaluate(n);
        }

        // --- Moon ---
        ApplyMoon(n);

        // --- Environment ---
        UpdateFogAndLighting(n);
    }

    void ApplyMoon(float n)
    {
        if (moon == null) return;

        // Moon is roughly opposite the sun (+90 so midnight = overhead)
        float moonAngle = n * 360f + 90f;
        moon.transform.rotation = Quaternion.Euler(moonAngle, 45f + moonYawOffset, 0f);

        // Remap so moonCurveT = 0.5 at midnight regardless of wall-clock wrap
        float moonCurveT = Mathf.Repeat(n + 0.5f, 1f);
        moon.intensity = moonIntensityOverNight.Evaluate(moonCurveT) * 0.15f;
        moon.color = moonColorOverNight.Evaluate(n);
    }

    // Returns a bell-shaped 0..1 value: 0 during day, rising to 1 at midnight.
    float ComputeNightProgress(float n)
    {
        float nt = Mathf.Clamp01(nightThreshold);
        if (nt <= 0f) return 0f;

        // Late-night segment (approaching midnight from the evening side)
        if (n >= 1f - nt)
            return Mathf.InverseLerp(1f - nt, 1f, n);

        // Early-night segment (retreating from midnight toward morning)
        if (n <= nt)
            // InverseLerp(nt, 0, n): at n=0 returns 1, at n=nt returns 0 — correct bell shape
            return Mathf.InverseLerp(nt, 0f, n);

        return 0f;
    }

    void UpdateFogAndLighting(float n)
    {
        float rawNightProgress = ComputeNightProgress(n);
        float nightProgress = Mathf.Pow(
            Mathf.SmoothStep(0f, 1f, rawNightProgress),
            Mathf.Max(0.0001f, nightFogSmoothness)
        );

        float sunScale = sunIntensityOverDay.Evaluate(n);
        float moonScale = nightProgress;

        // --- 1. Fog ---
        if (enableFog)
        {
            Color dayFog = fogColorOverDay.Evaluate(n);
            Color nightFog = new Color(0.03f, 0.04f, 0.07f);
            RenderSettings.fogColor = Color.Lerp(dayFog, nightFog, nightProgress);
            RenderSettings.fogDensity = fogDensityOverDay.Evaluate(n)
                                        * Mathf.Lerp(1f, nightFogMultiplier, nightProgress);
        }

        // --- 2. Ambient ---
        float ambientFloor = 0.15f;
        RenderSettings.ambientIntensity = Mathf.Lerp(
            ambientFloor, 1f,
            sunScale > 0.1f ? sunScale : moonScale * 0.35f
        );

        // --- 3. Skybox ---
        // Smoothly lerp exposure between full day and a faint night glow.
        // skyboxDayThreshold  -> full day exposure (1.0)
        // skyboxNightThreshold -> night exposure (nightSkyExposure)
        if (RenderSettings.skybox != null)
        {
            float skyT = Mathf.InverseLerp(skyboxNightThreshold, skyboxDayThreshold, sunScale);
            float skyExp = Mathf.Lerp(nightSkyExposure, 1.0f, skyT);
            float skyAtmo = Mathf.Lerp(nightAtmosphericThickness, 1f, skyT);

            // Only write material properties when they change meaningfully (avoid per-frame material dirtying)
            if (Mathf.Abs(skyExp - _lastSkyExposure) > 0.001f)
            {
                RenderSettings.skybox.SetFloat("_Exposure", skyExp);
                RenderSettings.skybox.SetFloat("_AtmosphericThickness", skyAtmo);

                _lastSkyExposure = skyExp;
            }
        }

        // --- 4. Primary sun swap (fires only once per transition) ---
        bool wantSun = nightProgress <= 0.5f;
        if (wantSun != _sunIsActive)
        {
            _sunIsActive = wantSun;
            RenderSettings.sun = wantSun ? sun : moon;
            DynamicGI.UpdateEnvironment();
        }
    }

    // -------------------------------------------------------------------------
    // Default curves / gradients
    // -------------------------------------------------------------------------

    static Gradient DefaultSunGradient()
    {
        Gradient g = new Gradient();
        g.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(0.05f, 0.05f, 0.12f), 0f),
                new GradientColorKey(new Color(1f,    0.5f,  0.2f),  0.2f),
                new GradientColorKey(new Color(1f,    1f,    0.95f), 0.5f),
                new GradientColorKey(new Color(1f,    0.5f,  0.2f),  0.8f),
                new GradientColorKey(new Color(0.05f, 0.05f, 0.12f), 1f)
            },
            new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
        );
        return g;
    }

    static AnimationCurve DefaultIntensityCurve()
    {
        return new AnimationCurve(
            new Keyframe(0f, 0.05f),
            new Keyframe(0.2f, 0.6f),
            new Keyframe(0.5f, 1f),
            new Keyframe(0.8f, 0.6f),
            new Keyframe(1f, 0.05f)
        );
    }

    static Gradient DefaultFogGradient()
    {
        Gradient g = new Gradient();
        g.SetKeys(
            new GradientColorKey[]
            {
            new GradientColorKey(new Color(0.07f, 0.08f, 0.09f), 0f),   // midnight: dark
            new GradientColorKey(new Color(0.55f, 0.42f, 0.28f), 0.22f), // sunrise: warm amber
            new GradientColorKey(new Color(0.45f, 0.48f, 0.42f), 0.5f),  // midday: muted olive-grey, not white
            new GradientColorKey(new Color(0.52f, 0.38f, 0.25f), 0.78f), // sunset: warm again
            new GradientColorKey(new Color(0.07f, 0.08f, 0.09f), 1f)    // midnight: dark
            },
            new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
        );
        return g;
    }

    static AnimationCurve DefaultFogDensityCurve()
    {
        return new AnimationCurve(
            new Keyframe(0f, 0.002f),
            new Keyframe(0.2f, 0.01f),
            new Keyframe(0.5f, 0.0015f),
            new Keyframe(0.8f, 0.01f),
            new Keyframe(1f, 0.002f)
        );
    }

    static Gradient DefaultMoonGradient()
    {
        Gradient g = new Gradient();
        g.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(0.6f, 0.65f, 0.8f), 0f),
                new GradientColorKey(new Color(0.6f, 0.65f, 0.8f), 1f)
            },
            new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
        );
        return g;
    }

    static AnimationCurve DefaultMoonIntensityCurve()
    {
        return new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.25f, 0.2f),
            new Keyframe(0.5f, 0.35f),
            new Keyframe(0.75f, 0.2f),
            new Keyframe(1f, 0f)
        );
    }
}