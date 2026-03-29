using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class CaptureSequence : MonoBehaviour
{
    public static CaptureSequence Instance { get; private set; }

    [Header("Overlay")]
    public float fadeDuration = 0.5f;

    [Header("Drag animation")]
    public float dragAnimDuration = 5f;
    public float dragAnimDistance = 1.0f;
    [Tooltip("How much the camera bumps vertically as the body is dragged along the ground.")]
    public float dragBumpHeight = 0.08f;
    [Tooltip("How fast the bumping rhythm is (higher = more frequent thuds).")]
    public float dragBumpFrequency = 3.5f;
    [Tooltip("How much the camera rolls side to side while being dragged.")]
    public float dragRollAmount = 6f;
    [Tooltip("How low the camera drops to simulate being on the ground.")]
    public float groundCameraHeight = 0.12f;

    [Header("Getting up")]
    public float getUpDuration = 2.2f;

    [Header("Vignette")]
    [Tooltip("Find this on your URP Global Volume in the scene.")]
    public Volume globalVolume;
    [Tooltip("Max vignette darkness.")]
    public float vignetteMaxIntensity = 0.65f;

    [Header("Optional inspector target")]
    public Transform inspectorDragDestination;

    [Header("Optional stalker teleport")]
    public Transform inspectorStalkerDestination;

    Canvas overlayCanvas;
    Image overlayImage;
    bool running;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) Destroy(this);
        CreateOverlay();

        // auto-find global volume if not assigned
        if (globalVolume == null)
            globalVolume = FindObjectOfType<Volume>();
    }

    void CreateOverlay()
    {
        GameObject canvasGO = new GameObject("CaptureOverlayCanvas");
        canvasGO.transform.SetParent(transform, false);
        overlayCanvas = canvasGO.AddComponent<Canvas>();
        overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        GameObject imgGO = new GameObject("BlackOverlay");
        imgGO.transform.SetParent(canvasGO.transform, false);
        overlayImage = imgGO.AddComponent<Image>();
        overlayImage.color = new Color(0f, 0f, 0f, 0f);
        RectTransform rt = overlayImage.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        overlayCanvas.enabled = false;
    }

    public static void Trigger(Transform player, Transform stalker,
        Transform dragDestination = null, Transform stalkerDestination = null)
    {
        if (Instance == null)
        {
            var gm = GameObject.FindObjectOfType<GameManager>();
            if (gm != null) Instance = gm.gameObject.AddComponent<CaptureSequence>();
            else Instance = new GameObject("CaptureSequence").AddComponent<CaptureSequence>();
        }
        Instance.StartCapture(player, stalker, dragDestination, stalkerDestination);
    }

    public void StartCapture(Transform player, Transform stalker,
        Transform dragDestination = null, Transform stalkerDestination = null)
    {
        if (running) return;
        StartCoroutine(CaptureRoutine(player, stalker, dragDestination, stalkerDestination));
    }

    IEnumerator CaptureRoutine(Transform player, Transform stalker,
        Transform dragDestination, Transform stalkerDestination)
    {
        running = true;

        if (player == null) { running = false; yield break; }

        // --- Player root ---
        Transform playerRoot = player;
        var pmCheck = player.GetComponent<PlayerMovement>();
        if (pmCheck == null)
        {
            var pmParent = player.GetComponentInParent<PlayerMovement>();
            if (pmParent != null) playerRoot = pmParent.transform;
        }

        // --- Destinations ---
        Vector3 finalWakePos = playerRoot.position;
        if (dragDestination != null) finalWakePos = dragDestination.position;
        else if (inspectorDragDestination != null) finalWakePos = inspectorDragDestination.position;

        Vector3 finalStalkerPos = Vector3.zero;
        bool haveStalkerDestination = false;
        if (stalkerDestination != null) { finalStalkerPos = stalkerDestination.position; haveStalkerDestination = true; }
        else if (inspectorStalkerDestination != null) { finalStalkerPos = inspectorStalkerDestination.position; haveStalkerDestination = true; }

        var gm = GameManager.Instance;
        if (gm != null) gm.SetState(GameState.Caught);

        // --- Disable player ---
        var pm = playerRoot.GetComponent<PlayerMovement>();
        if (pm != null) pm.enabled = false;

        // --- Camera setup ---
        var playerCamComp = FindObjectOfType<PlayerCam>();
        Transform camTf = playerCamComp != null ? playerCamComp.transform : Camera.main?.transform;

        MoveCamera moveCamScript = null;
        Transform cameraHolder = null;
        if (playerCamComp != null && playerCamComp.transform.parent != null)
        {
            cameraHolder = playerCamComp.transform.parent;
            moveCamScript = cameraHolder.GetComponent<MoveCamera>();
        }

        if (moveCamScript != null) moveCamScript.enabled = false;
        if (playerCamComp != null) playerCamComp.enabled = false;

        Quaternion camOriginalRotation = camTf != null ? camTf.rotation : Quaternion.identity;
        if (playerCamComp != null) playerCamComp.SetRotation(camOriginalRotation);

        // --- Physics ---
        var rb = playerRoot.GetComponent<Rigidbody>();
        var cc = playerRoot.GetComponent<CharacterController>();

        RigidbodyConstraints originalConstraints = RigidbodyConstraints.None;
        if (rb != null)
        {
            originalConstraints = rb.constraints;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            rb.AddForce((-playerRoot.forward + Vector3.up * 0.5f) * 2f, ForceMode.Impulse);
        }

        // --- Impact: fade to black ---
        overlayCanvas.enabled = true;
        yield return Fade(0f, 1f, fadeDuration);
        yield return new WaitForSeconds(0.9f);

        // --- Reveal: camera slams to ground level looking up ---
        yield return Fade(1f, 0f, fadeDuration);

        float yawAtCapture = camOriginalRotation.eulerAngles.y;

        // drop camera holder to ground level
        if (cameraHolder != null)
        {
            Vector3 groundPos = playerRoot.position;
            // raycast to find actual ground
            if (Physics.Raycast(playerRoot.position + Vector3.up * 2f, Vector3.down, out RaycastHit floorHit, 10f))
                groundPos.y = floorHit.point.y + groundCameraHeight;
            cameraHolder.position = groundPos;
        }

        // camera looks straight up (flat on back on the ground)
        if (camTf != null)
        {
            Quaternion onGround = Quaternion.Euler(-90f, yawAtCapture, 0f);
            camTf.rotation = onGround;
            if (playerCamComp != null) playerCamComp.SetRotation(onGround);
        }

        // --- Drag animation ---
        if (gm != null) gm.SetState(GameState.Dragging);

        SetVignette(vignetteMaxIntensity);

        float elapsed = 0f;
        Vector3 dragStartPos = cameraHolder != null ? cameraHolder.position : playerRoot.position;

        // direction of drag: toward the wake destination, flat
        Vector3 towardFinal = finalWakePos - playerRoot.position;
        towardFinal.y = 0f;
        Vector3 dragDir = towardFinal.sqrMagnitude > 0.001f
            ? towardFinal.normalized
            : playerRoot.forward;

        // also move the player root so the body slides
        Vector3 playerDragStart = playerRoot.position;

        while (elapsed < dragAnimDuration)
        {
            float t = elapsed / dragAnimDuration;

            // --- player body slides along ground ---
            Vector3 bodyTarget = playerDragStart + dragDir * (dragAnimDistance * Mathf.SmoothStep(0f, 1f, t));
            if (rb != null)
                rb.MovePosition(Vector3.Lerp(rb.position, bodyTarget, Time.deltaTime * 6f));
            else if (cc != null)
            {
                cc.enabled = false;
                playerRoot.position = Vector3.Lerp(playerRoot.position, bodyTarget, Time.deltaTime * 6f);
                cc.enabled = true;
            }
            else
                playerRoot.position = Vector3.Lerp(playerRoot.position, bodyTarget, Time.deltaTime * 6f);

            // --- camera stays at ground level, slides with body ---
            if (cameraHolder != null)
            {
                // ground height at current player position
                float groundY = playerRoot.position.y;
                if (Physics.Raycast(playerRoot.position + Vector3.up * 2f, Vector3.down, out RaycastHit gh, 10f))
                    groundY = gh.point.y + groundCameraHeight;

                // bump: sharp jolts like scraping over ground
                float bump = Mathf.Abs(Mathf.Sin(t * Mathf.PI * dragBumpFrequency * dragAnimDuration)) * dragBumpHeight;
                // bump fades out toward end (losing energy)
                bump *= (1f - Mathf.SmoothStep(0.6f, 1f, t));

                Vector3 camGroundPos = playerRoot.position;
                camGroundPos.y = groundY + bump;
                cameraHolder.position = Vector3.Lerp(cameraHolder.position, camGroundPos, Time.deltaTime * 10f);
            }

            // --- camera rotation: looking up with roll sway and bump tilt ---
            if (camTf != null)
            {
                // roll sways side to side slowly (like body rotating slightly as dragged)
                float roll = Mathf.Sin(t * Mathf.PI * dragBumpFrequency * 0.5f * dragAnimDuration) * dragRollAmount;
                // very slight pitch variation (bump tilt)
                float bumpPitch = Mathf.Sin(t * Mathf.PI * dragBumpFrequency * dragAnimDuration) * 4f;
                bumpPitch *= (1f - Mathf.SmoothStep(0.6f, 1f, t));

                Quaternion dragging = Quaternion.Euler(-90f + bumpPitch, yawAtCapture, roll);
                camTf.rotation = Quaternion.Slerp(camTf.rotation, dragging, Time.deltaTime * 8f);
                if (playerCamComp != null) playerCamComp.SetRotation(camTf.rotation);
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        ClearVignette();

        // --- Fade to black before teleport ---
        yield return Fade(0f, 1f, fadeDuration);

        // --- Teleport player ---
        Vector3 wakePosition = finalWakePos;
        if (Physics.Raycast(wakePosition + Vector3.up * 5f, Vector3.down, out RaycastHit groundHit, 20f))
            wakePosition.y = groundHit.point.y + 0.02f;

        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }

        if (cc != null) { cc.enabled = false; playerRoot.position = wakePosition; cc.enabled = true; }
        else if (rb != null) { rb.position = wakePosition; }
        else { playerRoot.position = wakePosition; }

        float wakeYaw = camOriginalRotation.eulerAngles.y;
        Quaternion uprightRotation = Quaternion.Euler(0f, wakeYaw, 0f);
        playerRoot.rotation = uprightRotation;
        if (rb != null)
        {
            rb.rotation = uprightRotation;
            rb.angularVelocity = Vector3.zero;
            rb.constraints = originalConstraints;
        }

        // --- Snap camera to CameraPos ---
        Transform cameraPosTarget = playerRoot.Find("CameraPos");
        if (cameraPosTarget != null && cameraHolder != null)
            cameraHolder.position = cameraPosTarget.position;
        else if (cameraHolder != null)
            cameraHolder.position = wakePosition + Vector3.up * 1.6f;

        // camera still looking up at new position
        if (camTf != null)
        {
            Quaternion lookUpAtNewPos = Quaternion.Euler(-75f, wakeYaw, 0f);
            camTf.rotation = lookUpAtNewPos;
            if (playerCamComp != null) playerCamComp.SetRotation(lookUpAtNewPos);
        }

        // --- Teleport stalker ---
        if (stalker != null && haveStalkerDestination)
        {
            var stalkerMove = stalker.GetComponent<StalkerMove>();
            if (stalkerMove != null) stalkerMove.ReturnToPatrol(finalStalkerPos);
            else
            {
                var agent = stalker.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (agent != null) { agent.Warp(finalStalkerPos); agent.ResetPath(); agent.isStopped = false; }
                else stalker.position = finalStalkerPos;
            }
        }

        // --- Clear vignette ---
        ClearVignette();

        // --- Getting up ---
        if (gm != null) gm.SetState(GameState.GettingUp);

        float getUpElapsed = 0f;
        Quaternion startCamRot = camTf != null ? camTf.rotation : Quaternion.identity;
        Quaternion targetCamRot = Quaternion.Euler(0f, wakeYaw, 0f);

        while (getUpElapsed < getUpDuration)
        {
            float p = Mathf.SmoothStep(0f, 1f, getUpElapsed / getUpDuration);

            if (camTf != null)
            {
                Quaternion q = Quaternion.Slerp(startCamRot, targetCamRot, p);
                camTf.rotation = q;
                if (playerCamComp != null) playerCamComp.SetRotation(q);
            }

            if (cameraPosTarget != null && cameraHolder != null)
                cameraHolder.position = cameraPosTarget.position;

            getUpElapsed += Time.deltaTime;
            yield return null;
        }

        if (camTf != null)
        {
            camTf.rotation = targetCamRot;
            if (playerCamComp != null) playerCamComp.SetRotation(targetCamRot);
        }

        // --- Restore everything ---
        if (gm != null) gm.SetState(GameState.EscapeSequence);

        ClearVignette();

        if (rb != null) rb.angularVelocity = Vector3.zero;
        if (pm != null) pm.enabled = true;
        if (playerCamComp != null) playerCamComp.enabled = true;
        if (moveCamScript != null) moveCamScript.enabled = true;

        yield return Fade(1f, 0f, fadeDuration);
        overlayCanvas.enabled = false;
        running = false;
    }

    void SetVignette(float intensity)
    {
        if (globalVolume == null) return;
        if (globalVolume.profile.TryGet<Vignette>(out var vig))
        {
            vig.intensity.value = intensity;
            vig.active = true;
        }
    }

    void ClearVignette()
    {
        if (globalVolume == null) return;
        if (globalVolume.profile.TryGet<Vignette>(out var vig))
        {
            vig.intensity.value = 0f;
            vig.active = false;
        }
    }

    IEnumerator Fade(float from, float to, float duration)
    {
        float time = 0f;
        Color c = overlayImage.color;
        while (time < duration)
        {
            overlayImage.color = new Color(c.r, c.g, c.b, Mathf.Lerp(from, to, time / duration));
            time += Time.deltaTime;
            yield return null;
        }
        overlayImage.color = new Color(c.r, c.g, c.b, to);
    }
}