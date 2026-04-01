using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed;
    public float groundDrag;
    public float jumpForce;
    public float jumpCooldown;
    public float airMultiplier;
    public float sprintMultiplier;
    bool readyToJump = true;

    [Header("Keybinds")]
    public KeyCode jumpKey = KeyCode.Space;
    public KeyCode sprintKey = KeyCode.LeftShift;
    public KeyCode crouchKey = KeyCode.LeftControl;

    [Header("Ground check")]
    public float playerHeight;
    public LayerMask whatIsGround;
    bool grounded;

    [Header("Slope Handling")]
    [Tooltip("Maximum slope angle (degrees) the player can walk up comfortably.")]
    public float maxSlopeAngle = 50f;
    [Tooltip("Extra downward force applied while on slopes to keep player grounded.")]
    public float slopeDownForce = 5f;

    [Header("Quality of Life")]
    [Tooltip("How quickly the player velocity is damped when no input is given.")]
    public float stopDamping = 10f;

    [Header("Stamina")]
    [Tooltip("Maximum stamina available to the player.")]
    public float maxStamina = 100f;
    [Tooltip("Stamina drained per second while sprinting.")]
    public float staminaDrainRate = 18f;
    [Tooltip("Stamina regenerated per second when not sprinting.")]
    public float staminaRegenRate = 12f;
    [Tooltip("Minimum stamina required to start/keep sprinting.")]
    public float minSprintStamina = 8f;
    [Tooltip("Cooldown time (seconds) after stamina fully depletes before regeneration starts.")]
    public float staminaCooldown = 0.5f;
    public float currentStamina;

    [Header("Crouch")]
    [Tooltip("Multiplier to movement speed while crouching.")]
    public float crouchSpeedMultiplier = 0.45f;
    [Tooltip("Multiplier applied to player height/raycast when crouching (for ground check).")]
    public float crouchHeightMultiplier = 0.6f;
    [Tooltip("The camera or eye transform to lower when crouching. Assign your Camera transform here.")]
    public Transform cameraTransform;
    [Tooltip("Local Y position of the camera when standing.")]
    public float standingCameraY = 0.7f;
    [Tooltip("Local Y position of the camera when crouching.")]
    public float crouchingCameraY = 0.1f;
    [Tooltip("How fast the camera lerps between standing and crouching positions.")]
    public float crouchCameraSpeed = 10f;

    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip[] footstepSounds;

    [Tooltip("Base time between footsteps")]
    public float stepInterval = 0.5f;

    [Tooltip("Sprint step speed multiplier")]
    public float sprintStepMultiplier = 0.6f;

    [Tooltip("Crouch step speed multiplier")]
    public float crouchStepMultiplier = 1.5f;

    [Tooltip("Crouch noise multiplier")]
    public float crouchNoiseMultiplier = 0.6f;

    [Tooltip("Base volume")]
    [Range(0f, 1f)] public float baseVolume = 0.7f;

    [Header("UI")]
    public UnityEngine.UI.Slider staminaBar;

    float stepTimer;

    public Transform orientation;

    float horizontalInput;
    float verticalInput;

    Vector3 moveDirection;
    Rigidbody rb;

    bool isSprinting;
    bool sprintDisabledByStamina;
    float currentMoveSpeed;

    // slope check internals
    private RaycastHit slopeHit;
    private bool onSlope;
    private Vector3 slopeMoveDirection;

    private float staminaDepletedTime = -999f;

    // Expose current velocity so other systems (e.g. AI) can query player motion without CharacterController
    public Vector3 CurrentVelocity => rb != null ? rb.linearVelocity : Vector3.zero;

    // Crouch state exposed for AI
    bool isCrouching;
    public bool IsCrouching => isCrouching;

    // Exposed noise level for AI hearing (simple approximation)
    public float NoiseLevel => rb != null ? rb.linearVelocity.magnitude * (isCrouching ? crouchNoiseMultiplier : 1f) : 0f;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;
        currentMoveSpeed = moveSpeed;
        currentStamina = maxStamina;
        if (staminaBar != null)
        {
            staminaBar.maxValue = maxStamina;
            staminaBar.value = currentStamina;
        }
    }

    void Update()
    {
        // Prevent player input and player-driven movement when the game is not in a playable movement state
        if (GameManager.Instance != null &&
            GameManager.Instance.currentState != GameState.Exploration &&
            GameManager.Instance.currentState != GameState.EscapeSequence &&
            GameManager.Instance.currentState != GameState.Chased)
        {
            horizontalInput = 0f;
            verticalInput = 0f;
            isSprinting = false;
            return;
            
        }

        grounded = Physics.Raycast(transform.position, Vector3.down, playerHeight * 0.5f + 0.2f, whatIsGround);

        MyInput();
        SpeedControl();
        HandleCrouchCamera();

        if (grounded)
            rb.linearDamping = groundDrag;
        else
            rb.linearDamping = 0;

        onSlope = OnSlope();

        HandleStamina();
        HandleFootsteps();
    }

    private void FixedUpdate()
    {
        if (GameManager.Instance != null &&
            GameManager.Instance.currentState != GameState.Exploration &&
            GameManager.Instance.currentState != GameState.EscapeSequence &&
            GameManager.Instance.currentState != GameState.Chased)
        {
            return;
        }

        MovePlayer();
        ApplyStopDamping();
    }

    private void MyInput()
    {
        horizontalInput = Input.GetAxisRaw("Horizontal");
        verticalInput = Input.GetAxisRaw("Vertical");

        // Hold to crouch — cannot sprint while crouching
        isCrouching = Input.GetKey(crouchKey);

        // Sprinting requires: key held, forward input, not crouching, stamina available
        bool wantsToSprint = Input.GetKey(sprintKey) && verticalInput > 0f && !isCrouching;
        isSprinting = wantsToSprint && !sprintDisabledByStamina && currentStamina > minSprintStamina;

        // Determine effective move speed
        if (isCrouching)
        {
            // Crouching: reduced speed, no sprint
            currentMoveSpeed = moveSpeed * crouchSpeedMultiplier;
        }
        else if (isSprinting)
        {
            currentMoveSpeed = moveSpeed * sprintMultiplier;
        }
        else
        {
            currentMoveSpeed = moveSpeed;
        }

        // Jumping disabled while crouching
        if (Input.GetKey(jumpKey) && readyToJump && grounded && !isCrouching)
        {
            readyToJump = false;
            Jump();
            Invoke(nameof(ResetJump), jumpCooldown);
        }
    }

    private void HandleCrouchCamera()
    {
        if (cameraTransform == null) return;

        float targetY = isCrouching ? crouchingCameraY : standingCameraY;
        Vector3 localPos = cameraTransform.localPosition;
        localPos.y = Mathf.Lerp(localPos.y, targetY, Time.deltaTime * crouchCameraSpeed);
        cameraTransform.localPosition = localPos;
    }

    private void MovePlayer()
    {
        moveDirection = orientation.forward * verticalInput + orientation.right * horizontalInput;

        if (grounded && onSlope && moveDirection.sqrMagnitude > 0f)
        {
            slopeMoveDirection = Vector3.ProjectOnPlane(moveDirection, slopeHit.normal).normalized;
            rb.AddForce(slopeMoveDirection * currentMoveSpeed * 10f, ForceMode.Force);
            rb.AddForce(Vector3.down * slopeDownForce, ForceMode.Force);
        }
        else if (grounded)
        {
            rb.AddForce(moveDirection.normalized * currentMoveSpeed * 10f, ForceMode.Force);
        }
        else
        {
            rb.AddForce(moveDirection.normalized * currentMoveSpeed * 10f * airMultiplier, ForceMode.Force);
        }
    }

    private void SpeedControl()
    {
        Vector3 flatVel = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

        if (flatVel.magnitude > currentMoveSpeed)
        {
            Vector3 limitedVel = flatVel.normalized * currentMoveSpeed;
            rb.linearVelocity = new Vector3(limitedVel.x, rb.linearVelocity.y, limitedVel.z);
        }
    }

    private void Jump()
    {
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
    }

    private void ResetJump()
    {
        readyToJump = true;
    }

    private bool OnSlope()
    {
        if (!grounded) return false;

        float checkDistance = playerHeight * 0.5f + 0.3f;
        if (Physics.Raycast(transform.position, Vector3.down, out slopeHit, checkDistance, whatIsGround))
        {
            float angle = Vector3.Angle(Vector3.up, slopeHit.normal);
            return angle > 0f && angle <= maxSlopeAngle;
        }

        return false;
    }

    private void ApplyStopDamping()
    {
        if (grounded && Mathf.Abs(horizontalInput) < 0.01f && Mathf.Abs(verticalInput) < 0.01f)
        {
            Vector3 lv = rb.linearVelocity;
            float damping = Mathf.Clamp01(Time.fixedDeltaTime * stopDamping);
            float newX = Mathf.Lerp(lv.x, 0f, damping);
            float newZ = Mathf.Lerp(lv.z, 0f, damping);
            rb.linearVelocity = new Vector3(newX, lv.y, newZ);
        }
    }

    private void HandleStamina()
    {
        if (isSprinting && verticalInput > 0f)
        {
            currentStamina -= staminaDrainRate * Time.deltaTime;
            if (currentStamina <= 0f)
            {
                currentStamina = 0f;
                sprintDisabledByStamina = true;
                staminaDepletedTime = Time.time;
                isSprinting = false;
                currentMoveSpeed = moveSpeed;
            }
        }
        else
        {
            if (sprintDisabledByStamina)
            {
                if (Time.time >= staminaDepletedTime + staminaCooldown)
                    sprintDisabledByStamina = false;
            }

            if (!sprintDisabledByStamina)
            {
                currentStamina += staminaRegenRate * Time.deltaTime;
                currentStamina = Mathf.Clamp(currentStamina, 0f, maxStamina);
            }
        }
        if (staminaBar != null)
        {
            staminaBar.value = Mathf.Lerp(staminaBar.value, currentStamina, Time.deltaTime * 10f);
        }
    }

    private void HandleFootsteps()
    {
        Vector3 flatVel = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        bool isMoving = flatVel.magnitude > 0.1f;

        if (!grounded || !isMoving)
        {
            stepTimer = 0f;
            return;
        }

        float currentStepInterval = stepInterval;

        if (isSprinting && currentStamina > 1)
            currentStepInterval *= sprintStepMultiplier;
        else if (isCrouching)
            currentStepInterval *= crouchStepMultiplier;

        stepTimer += Time.deltaTime;

        if (stepTimer >= currentStepInterval)
        {
            float volume = baseVolume;

            if (isSprinting)
                volume *= 1.2f;
            else if (isCrouching)
                volume *= crouchNoiseMultiplier;

            if (footstepSounds.Length == 0) return;

            AudioClip clip = footstepSounds[Random.Range(0, footstepSounds.Length)];
            audioSource.PlayOneShot(clip, volume);

            stepTimer = 0f;
        }
    }
}