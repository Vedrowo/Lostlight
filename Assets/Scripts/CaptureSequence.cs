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
    [Tooltip("How fast the bumping rhythm is.")]
    public float dragBumpFrequency = 3.5f;
    [Tooltip("How much the camera rolls side to side while being dragged.")]
    public float dragRollAmount = 6f;
    [Tooltip("How low the camera drops to simulate being on the ground.")]
    public float groundCameraHeight = 0.12f;

    [Header("Getting up")]
    public float getUpDuration = 2.2f;

    [Header("Vignette")]
    public Volume globalVolume;
    public float vignetteIntensity = 0.65f;

    [Header("Optional inspector target")]
    public Transform inspectorDragDestination;

    [Header("Optional stalker teleport")]
    public Transform inspectorStalkerDestination;

    Canvas overlayCanvas;
    Image overlayImage;
    bool running;

    // cached references — populated in Awake to avoid mid-coroutine FindObjectOfType calls
    PlayerCam playerCamComp;
    MoveCamera moveCamScript;
    Transform cameraHolder;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) Destroy(this);

        CreateOverlay();

        if (globalVolume == null)
            globalVolume = FindObjectOfType<Volume>();

        // cache camera references once at startup
        playerCamComp = FindObjectOfType<PlayerCam>();
        if (playerCamComp != null && playerCamComp.transform.parent != null)
        {
            cameraHolder = playerCamComp.transform.parent;
            moveCamScript = cameraHolder.GetComponent<MoveCamera>();
        }
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

        // use cached camera references; re-find only if null (e.g. scene reload)
        if (playerCamComp == null) playerCamComp = FindObjectOfType<PlayerCam>();
        if (playerCamComp != null && cameraHolder == null)
        {
            cameraHolder = playerCamComp.transform.parent;
            if (cameraHolder != null) moveCamScript = cameraHolder.GetComponent<MoveCamera>();
        }

        Transform camTf = playerCamComp != null ? playerCamComp.transform : Camera.main?.transform;

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

        // --- Reveal: camera slams to ground ---
        yield return Fade(1f, 0f, fadeDuration);

        float yawAtCapture = camOriginalRotation.eulerAngles.y;

        if (cameraHolder != null)
        {
            Vector3 groundPos = playerRoot.position;
            if (Physics.Raycast(playerRoot.position + Vector3.up * 2f, Vector3.down, out RaycastHit floorHit, 10f))
                groundPos.y = floorHit.point.y + groundCameraHeight;
            cameraHolder.position = groundPos;
        }

        if (camTf != null)
        {
            Quaternion onGround = Quaternion.Euler(-90f, yawAtCapture, 0f);
            camTf.rotation = onGround;
            if (playerCamComp != null) playerCamComp.SetRotation(onGround);
        }

        // --- Drag animation ---
        if (gm != null) gm.SetState(GameState.Dragging);

        SetVignette(vignetteIntensity);

        float elapsed = 0f;
        Vector3 dragDir = finalWakePos - playerRoot.position;
        dragDir.y = 0f;
        dragDir = dragDir.sqrMagnitude > 0.001f ? dragDir.normalized : playerRoot.forward;
        Vector3 playerDragStart = playerRoot.position;

        while (elapsed < dragAnimDuration)
        {
            float t = elapsed / dragAnimDuration;

            // slide body
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

            // camera follows at ground level with bumps
            if (cameraHolder != null)
            {
                float groundY = playerRoot.position.y;
                if (Physics.Raycast(playerRoot.position + Vector3.up * 2f, Vector3.down, out RaycastHit gh, 10f))
                    groundY = gh.point.y + groundCameraHeight;

                float bump = Mathf.Abs(Mathf.Sin(t * Mathf.PI * dragBumpFrequency * dragAnimDuration)) * dragBumpHeight;
                bump *= (1f - Mathf.SmoothStep(0.6f, 1f, t));

                Vector3 camGroundPos = playerRoot.position;
                camGroundPos.y = groundY + bump;
                cameraHolder.position = Vector3.Lerp(cameraHolder.position, camGroundPos, Time.deltaTime * 10f);
            }

            // camera rotation with roll and bump tilt
            if (camTf != null)
            {
                float roll = Mathf.Sin(t * Mathf.PI * dragBumpFrequency * 0.5f * dragAnimDuration) * dragRollAmount;
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

        // snap camera holder to CameraPos on player
        Transform cameraPosTarget = playerRoot.Find("CameraPos");
        if (cameraPosTarget != null && cameraHolder != null)
            cameraHolder.position = cameraPosTarget.position;
        else if (cameraHolder != null)
            cameraHolder.position = wakePosition + Vector3.up * 1.6f;

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
                stalkerMove.ReturnToPatrol(finalStalkerPos);
            else
            {
                var agent = stalker.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (agent != null) { agent.Warp(finalStalkerPos); agent.ResetPath(); agent.isStopped = false; }
                else stalker.position = finalStalkerPos;
            }
        }

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

        // --- Restore ---
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