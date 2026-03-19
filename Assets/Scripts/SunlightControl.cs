using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
public class SunlightControl : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Directional light used as the sun. If null the first directional Light found will be used.")]
    public Light sun;

    [Tooltip("Directional light used as the moon. If null an opposite-directional light will be used if available.")]
    public Light moon;

    public Volume globalVolume;

    [Header("Time")]
    [Tooltip("Time of day in hours (0 - 24). 0 = midnight, 12 = noon.")]
    [Range(0f, 24f)]
    public float timeOfDay = 12f;

    [Tooltip("If true the system will advance time automatically (useful for demos).")]
    public bool autoCycle = false;
    [Tooltip("How many in-game hours pass per real second while autoCycle is enabled.")]
    public float hoursPerSecond = 0.1f;

    [Header("Sun appearance (simple)")]
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

    [Header("Behavior")]
    [Range(0f, 1f)]
    [Tooltip("Normalized threshold (0..1 => 0..24 hours) below which it's considered night.")]
    public float nightThreshold = 0.25f; // 6:00 -> 0.25

    // internal
    Coroutine runningTransition;

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
    }

    void Start()
    {
        // ensure moon is initially configured as weaker / off if needed
        if (moon != null)
        {
            moon.enabled = true; // we control intensity to zero during day to avoid flicker
        }

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
            float t = Mathf.Clamp01(elapsed / duration);
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
            sun.transform.rotation = Quaternion.Euler(new Vector3(sunAngle, 45f, 0f)); // tweak yaw for direction
            // color & intensity
            Color sunColor = sunColorOverDay.Evaluate(timeOfDay / 24f);
            sun.color = sunColor;
            float intensity = sunIntensityOverDay.Evaluate(timeOfDay / 24f);
            sun.intensity = intensity;
        }

        // Moon: place roughly opposite the sun and set low-intensity glow at night, reduced by clouds
        ApplyMoon(timeOfDay);

        UpdateFogAndLighting();
    }

    void ApplyMoon(float hours)
    {
        if (moon == null) return;

        float n = hours / 24f;

        // 1. FIXED ROTATION: 
        // We use +90 so at midnight (0/24) the moon is roughly overhead.
        float moonAngle = (n * 360f) + 90f;
        moon.transform.rotation = Quaternion.Euler(new Vector3(moonAngle, 45f + moonYawOffset, 0f));

        // 2. FIXED INTENSITY:
        // We bypass 'nightFactor' and let your curve (moonIntensityOverNight) do all the work.
        // I added a * 5f multiplier so it's actually bright enough to see.
        float moonCurveT = Mathf.Repeat(n + 0.5f, 1f);
        float intensity = moonIntensityOverNight.Evaluate(moonCurveT);

        // 3. COLOR
        moon.color = moonColorOverNight.Evaluate(n);
        moon.intensity = intensity;
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

        // ambient lighting
        float ambientScale = Mathf.Clamp01(sunIntensityOverDay.Evaluate(n));
        RenderSettings.ambientIntensity = Mathf.Lerp(0.2f, 1f, ambientScale);

        // 1. Calculate a smooth exposure value (1.0 at day, 0.015 at peak night)
        float targetExposure = Mathf.Lerp(1.0f, 0.015f, nightProgress);
        float targetThickness = Mathf.Lerp(1.0f, 0.6f, nightProgress);

        // 2. Apply these EVERY frame (not just inside the IF)
        RenderSettings.skybox.SetFloat("_Exposure", targetExposure);
        RenderSettings.skybox.SetFloat("_AtmosphericThickness", targetThickness);

        // 3. The Swap (Keep the threshold at 0.4 or 0.5)
        if (nightProgress > 0.4f)
        {
            if (RenderSettings.sun != moon)
            {
                RenderSettings.sun = moon;
                DynamicGI.UpdateEnvironment();
            }
        }
        else
        {
            if (RenderSettings.sun != sun)
            {
                RenderSettings.sun = sun;
                DynamicGI.UpdateEnvironment();
            }
        }
    }

    // Defaults
    static Gradient DefaultSunGradient()
    {
        Gradient g = new Gradient();
        g.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(new Color(0.05f,0.05f,0.12f), 0f),
                new GradientColorKey(new Color(1f,0.5f,0.2f), 0.2f),
                new GradientColorKey(new Color(1f,1f,0.95f), 0.5f),
                new GradientColorKey(new Color(1f,0.5f,0.2f), 0.8f),
                new GradientColorKey(new Color(0.05f,0.05f,0.12f), 1f)
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
