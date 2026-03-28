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
    [Tooltip("Noise multiplier applied when crouching (lower = quieter).")]
    public float crouchNoiseMultiplier = 0.35f;

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
    }

    void Update()
    {
        // Prevent player input and player-driven movement when the game is not in a playable movement state
        if (GameManager.Instance != null &&
            GameManager.Instance.currentState != GameState.Exploration &&
            GameManager.Instance.currentState != GameState.EscapeSequence)
        {
            // Clear input-related flags so player does not apply forces while another system (dragging/catching/etc.) moves them.
            horizontalInput = 0f;
            verticalInput = 0f;
            isSprinting = false;
            // keep physics and external movement intact (do not zero velocities) and skip player processing
            return;
        }

        float effectivePlayerHeight = playerHeight * (isCrouching ? crouchHeightMultiplier : 1f);
        grounded = Physics.Raycast(transform.position, Vector3.down, effectivePlayerHeight * 0.5f + 0.2f, whatIsGround);
        MyInput();
        SpeedControl();

        // apply drag when grounded for tighter control
        if (grounded)
        {
            rb.linearDamping = groundDrag;
        }
        else
        {
            rb.linearDamping = 0;
        }

        // check slope state for use in FixedUpdate
        onSlope = OnSlope();

        // Update stamina and sprint availability
        HandleStamina();
    }

    private void FixedUpdate()
    {
        // Prevent applying player-controlled forces when game state disallows player movement.
        if (GameManager.Instance != null &&
            GameManager.Instance.currentState != GameState.Exploration &&
            GameManager.Instance.currentState != GameState.EscapeSequence)
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

        // crouch is hold-to-crouch — movement is allowed while crouching, but speed and noise are reduced
        isCrouching = Input.GetKey(crouchKey);

        // decide sprinting only if forward input and key pressed and stamina allows it and not crouching
        bool wantsToSprint = Input.GetKey(sprintKey) && verticalInput > 0f && !isCrouching;
        isSprinting = wantsToSprint && !sprintDisabledByStamina && currentStamina > minSprintStamina;

        // base move speed (sprinting overrides)
        float baseSpeed = isSprinting ? moveSpeed * sprintMultiplier : moveSpeed;
        // apply crouch multiplier if crouching (movement still allowed)
        currentMoveSpeed = isCrouching ? baseSpeed * crouchSpeedMultiplier : baseSpeed;

        // jumping is disabled while crouching
        if (Input.GetKey(jumpKey) && readyToJump && grounded && !isCrouching)
        {
            readyToJump = false;
            Jump();
            Invoke(nameof(ResetJump), jumpCooldown);
        }
    }

    private void MovePlayer()
    {
        moveDirection = orientation.forward * verticalInput + orientation.right * horizontalInput;

        // if on a walkable slope, project movement onto slope plane so player moves along slope
        if (grounded && onSlope && moveDirection.sqrMagnitude > 0f)
        {
            // ensure we have a slopeMoveDirection calculated
            slopeMoveDirection = Vector3.ProjectOnPlane(moveDirection, slopeHit.normal).normalized;

            rb.AddForce(slopeMoveDirection * currentMoveSpeed * 10f, ForceMode.Force);
            // push player slightly downward so they don't "float" on slopes
            rb.AddForce(Vector3.down * slopeDownForce, ForceMode.Force);
        }
        else if (grounded)
        {
            rb.AddForce(moveDirection.normalized * currentMoveSpeed * 10f, ForceMode.Force);
        }
        else // in air
        {
            rb.AddForce(moveDirection.normalized * currentMoveSpeed * 10f * airMultiplier, ForceMode.Force);
        }
    }

    private void SpeedControl()
    {
        Vector3 flatVel = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

        // use currentMoveSpeed (so sprinting increases the cap)
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

    // returns true when the surface beneath is a slope within the allowed angle
    private bool OnSlope()
    {
        if (!grounded) return false;

        float checkDistance = playerHeight * 0.5f + 0.3f;
        if (Physics.Raycast(transform.position, Vector3.down, out slopeHit, checkDistance, whatIsGround))
        {
            float angle = Vector3.Angle(Vector3.up, slopeHit.normal);
            // allow walking on slopes up to maxSlopeAngle (exclusive of perfectly flat)
            return angle > 0f && angle <= maxSlopeAngle;
        }

        return false;
    }

    // gently damp horizontal velocity when no input is provided to reduce sliding and feel snappier
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

    // handle stamina drain/regen and disable sprinting if empty
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
            // if recently fully depleted, delay regen by staminaCooldown
            if (sprintDisabledByStamina)
            {
                if (Time.time >= staminaDepletedTime + staminaCooldown)
                {
                    sprintDisabledByStamina = false;
                }
            }

            if (!sprintDisabledByStamina)
            {
                currentStamina += staminaRegenRate * Time.deltaTime;
                currentStamina = Mathf.Clamp(currentStamina, 0f, maxStamina);
            }
        }
    }
}
