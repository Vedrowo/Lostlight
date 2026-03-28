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

    [Header("Drag animation (simplified)")]
    [Tooltip("Duration of the short drag animation shown before blackout.")]
    public float dragAnimDuration = 5f;
    [Tooltip("How far the player will subtly shift during the short drag animation.")]
    public float dragAnimDistance = 1.0f;
    [Tooltip("Vertical bob amplitude during the drag animation.")]
    public float dragBobHeight = 0.15f;

    [Header("Getting up")]
    [Tooltip("How long the 'getting up' animation takes once the player is teleported.")]
    public float getUpDuration = 2.2f;

    [Header("Optional inspector target")]
    [Tooltip("If set, this Transform will be used as the final location the player wakes up at.")]
    public Transform inspectorDragDestination;

    [Header("Optional stalker teleport")]
    [Tooltip("If set, this Transform will be used as the location the stalker is teleported to.")]
    public Transform inspectorStalkerDestination;

    Canvas overlayCanvas;
    Image overlayImage;
    bool running;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) Destroy(this);
        CreateOverlay();
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

        // --- Resolve player root ---
        Transform playerRoot = player;
        var pmCheck = player.GetComponent<PlayerMovement>();
        if (pmCheck == null)
        {
            var pmParent = player.GetComponentInParent<PlayerMovement>();
            if (pmParent != null) playerRoot = pmParent.transform;
        }

        // --- Resolve wake-up destination ---
        Vector3 finalWakePos = playerRoot.position;
        if (dragDestination != null) finalWakePos = dragDestination.position;
        else if (inspectorDragDestination != null) finalWakePos = inspectorDragDestination.position;

        // --- Resolve stalker destination ---
        Vector3 finalStalkerPos = Vector3.zero;
        bool haveStalkerDestination = false;
        if (stalkerDestination != null) { finalStalkerPos = stalkerDestination.position; haveStalkerDestination = true; }
        else if (inspectorStalkerDestination != null) { finalStalkerPos = inspectorStalkerDestination.position; haveStalkerDestination = true; }

        var gm = GameManager.Instance;
        if (gm != null) gm.SetState(GameState.Caught);

        // --- Disable player movement ---
        var pm = playerRoot.GetComponent<PlayerMovement>();
        if (pm != null) pm.enabled = false;

        // --- Resolve camera objects ---
        // CameraHolder is the root, PlayerCam is its child
        var playerCamComp = FindObjectOfType<PlayerCam>();
        Transform camTf = playerCamComp != null ? playerCamComp.transform : Camera.main?.transform;

        // Find CameraHolder (the follow script lives here — it's the parent of PlayerCam)
        MoveCamera moveCamScript = null;
        Transform cameraHolder = null;
        if (playerCamComp != null && playerCamComp.transform.parent != null)
        {
            cameraHolder = playerCamComp.transform.parent;
            moveCamScript = cameraHolder.GetComponent<MoveCamera>();
        }

        // Disable the camera follow script so it stops chasing the old position
        if (moveCamScript != null) moveCamScript.enabled = false;
        if (playerCamComp != null) playerCamComp.enabled = false;

        // Store original camera yaw so we can restore it at the end
        Quaternion camOriginalRotation = camTf != null ? camTf.rotation : Quaternion.identity;
        if (playerCamComp != null) playerCamComp.SetRotation(camOriginalRotation);

        // --- Resolve player physics ---
        var rb = playerRoot.GetComponent<Rigidbody>();
        var cc = playerRoot.GetComponent<CharacterController>();

        RigidbodyConstraints originalConstraints = RigidbodyConstraints.None;
        if (rb != null)
        {
            originalConstraints = rb.constraints;
            // Allow motion but freeze rotation so the body doesn't ragdoll-spin wildly
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            rb.AddForce((-playerRoot.forward + Vector3.up * 0.5f) * 2f, ForceMode.Impulse);
        }

        // --- Fade to black (impact) ---
        overlayCanvas.enabled = true;
        yield return Fade(0f, 1f, fadeDuration);
        yield return new WaitForSeconds(0.9f);

        // --- Reveal: show lying-down camera angle ---
        yield return Fade(1f, 0f, fadeDuration);

        if (camTf != null)
        {
            float yaw = camOriginalRotation.eulerAngles.y;
            Quaternion lookUp = Quaternion.Euler(-75f, yaw, 0f);
            camTf.rotation = lookUp;
            if (playerCamComp != null) playerCamComp.SetRotation(lookUp);
        }

        // --- Short drag animation ---
        if (gm != null) gm.SetState(GameState.Dragging);

        float elapsed = 0f;
        Vector3 startRootPos = playerRoot.position;
        Vector3 towardFinal = finalWakePos - startRootPos;
        towardFinal.y = 0f;
        Vector3 smallShift = towardFinal.sqrMagnitude > 0.001f
            ? towardFinal.normalized * dragAnimDistance
            : playerRoot.forward * dragAnimDistance;

        while (elapsed < dragAnimDuration)
        {
            float t = elapsed / dragAnimDuration;
            float shiftAmt = Mathf.SmoothStep(0f, 1f, t);
            Vector3 bob = Vector3.up * (Mathf.Sin(t * Mathf.PI * 4f) * dragBobHeight * (1f - t));
            Vector3 target = startRootPos + smallShift * shiftAmt + bob;

            if (rb != null)
                rb.MovePosition(Vector3.Lerp(rb.position, target, Time.deltaTime * 8f));
            else if (cc != null)
            {
                cc.enabled = false;
                playerRoot.position = Vector3.Lerp(playerRoot.position, target, Time.deltaTime * 8f);
                cc.enabled = true;
            }
            else
                playerRoot.position = Vector3.Lerp(playerRoot.position, target, Time.deltaTime * 8f);

            // Camera wobble (stays looking up)
            if (camTf != null)
            {
                float yaw = camOriginalRotation.eulerAngles.y;
                float extraTilt = Mathf.Sin(t * Mathf.PI * 2f) * 4f * (1f - t);
                Quaternion wobble = Quaternion.Euler(-75f - extraTilt, yaw, 0f);
                camTf.rotation = wobble;
                if (playerCamComp != null) playerCamComp.SetRotation(wobble);
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        // --- Fade to black before teleport ---
        yield return Fade(0f, 1f, fadeDuration);

        // --- Teleport player to wake position (snap to ground) ---
        Vector3 wakePosition = finalWakePos;
        if (Physics.Raycast(wakePosition + Vector3.up * 5f, Vector3.down, out RaycastHit groundHit, 20f))
            wakePosition.y = groundHit.point.y + 0.02f;

        // Fully zero out physics before teleporting
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (cc != null)
        {
            cc.enabled = false;
            playerRoot.position = wakePosition;
            cc.enabled = true;
        }
        else if (rb != null)
        {
            rb.position = wakePosition;
        }
        else
        {
            playerRoot.position = wakePosition;
        }

        // Reset player rotation to upright — CRITICAL to prevent sideways jumping
        float wakeYaw = camOriginalRotation.eulerAngles.y;
        Quaternion uprightRotation = Quaternion.Euler(0f, wakeYaw, 0f);
        playerRoot.rotation = uprightRotation;
        if (rb != null)
        {
            rb.rotation = uprightRotation;
            rb.angularVelocity = Vector3.zero;
            // Restore original constraints now so physics is clean before re-enabling movement
            rb.constraints = originalConstraints;
        }

        // --- Snap CameraHolder to the CameraPos transform on the player ---
        // This is the key fix: instead of guessing where the camera should be,
        // find the CameraPos child on the player and move the holder there.
        Transform cameraPosTarget = playerRoot.Find("CameraPos");
        if (cameraPosTarget != null && cameraHolder != null)
        {
            cameraHolder.position = cameraPosTarget.position;
        }
        else if (cameraHolder != null)
        {
            // Fallback: place camera holder at player position + a reasonable eye height
            cameraHolder.position = wakePosition + Vector3.up * 1.6f;
        }

        // Set camera to looking-up angle at new position
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
            if (stalkerMove != null)
            {
                stalkerMove.ReturnToPatrol(finalStalkerPos);
            }
            else
            {
                var agent = stalker.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (agent != null) { agent.Warp(finalStalkerPos); agent.ResetPath(); agent.isStopped = false; }
                else stalker.position = finalStalkerPos;
            }
        }

        // --- Clear vignette ---
        var ph = playerRoot.GetComponent<PlayerHealth>() ?? playerRoot.GetComponentInChildren<PlayerHealth>();
        if (ph != null && ph.globalVolume != null && ph.globalVolume.profile != null)
        {
            if (ph.globalVolume.profile.TryGet<Vignette>(out var vig))
            { vig.intensity.value = 0f; vig.active = false; }
        }

        // --- Getting up state ---
        if (gm != null) gm.SetState(GameState.GettingUp);

        // Animate camera from looking-up back to normal eye level
        float getUpElapsed = 0f;
        Quaternion startCamRot = camTf != null ? camTf.rotation : Quaternion.identity;
        Quaternion targetCamRot = Quaternion.Euler(0f, wakeYaw, 0f); // look straight ahead when standing

        while (getUpElapsed < getUpDuration)
        {
            float p = Mathf.SmoothStep(0f, 1f, getUpElapsed / getUpDuration);

            if (camTf != null)
            {
                Quaternion q = Quaternion.Slerp(startCamRot, targetCamRot, p);
                camTf.rotation = q;
                if (playerCamComp != null) playerCamComp.SetRotation(q);
            }

            // Also keep the CameraHolder snapped to CameraPos during get-up
            // (in case MoveCamera was doing this every frame before)
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

        // --- Final state: escape sequence ---
        if (gm != null) gm.SetState(GameState.EscapeSequence);

        // Clear vignette again (safety)
        if (ph != null && ph.globalVolume != null && ph.globalVolume.profile != null)
        {
            if (ph.globalVolume.profile.TryGet<Vignette>(out var vig2))
            { vig2.intensity.value = 0f; vig2.active = false; }
        }

        // Zero angular velocity one final time before handing control back
        if (rb != null) rb.angularVelocity = Vector3.zero;

        // Re-enable all controls
        if (pm != null) pm.enabled = true;
        if (playerCamComp != null) playerCamComp.enabled = true;
        if (moveCamScript != null) moveCamScript.enabled = true; // re-enable follow script last

        // Fade in and done
        yield return Fade(1f, 0f, fadeDuration);
        overlayCanvas.enabled = false;
        running = false;
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