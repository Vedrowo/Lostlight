using UnityEngine;

public class StalkerMove : MonoBehaviour
{
    public float speed;
    public float rotationSpeed = 5f;

    public string targetTag = "Player";
    Transform target;

    [Header("Detection")]
    public float detectionRadius = 40f; // start chasing inside this radius
    public float loseRadius = 50f;      // stop chasing when target goes beyond this
    [Tooltip("If true the stalker will constantly search for objects with `targetTag` each Update.")]
    public bool constantlySearchForTarget = true;

    [Header("Patrol")]
    public Transform[] patrolPoints;
    public bool patrolLoop = true;
    public float waypointTolerance = 0.5f;

    [Header("Chase")]
    [Tooltip("Multiplier applied to `speed` while chasing.")]
    public float chaseSpeedMultiplier = 1.5f;

    [Header("Grounding")]
    public float groundCheckDistance = 5f;
    public LayerMask groundMask;
    [Tooltip("How fast the stalker snaps to ground height (higher = faster).")]
    public float groundSnapSpeed = 20f;

    [Header("Animation")]
    [Tooltip("Animator component (assign in inspector or will attempt to find one on Start).")]
    public Animator animator;
    [Tooltip("Animator bool parameter name that triggers run animation when true.")]
    public string runParamName = "isRunning";

    int patrolIndex;
    bool isChasing;
    Transform currentTarget;

    void Start()
    {
        patrolIndex = 0;
        // if a matching tag exists in the scene, keep a reference if desired (target will be updated each frame)
        var initial = GameObject.FindGameObjectWithTag(targetTag);
        if (initial != null) target = initial.transform;

        if (animator == null)
            animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>();
    }

    void Update()
    {
        // constantly check for nearest target if configured
        if (constantlySearchForTarget)
            UpdateTarget();

        if (isChasing && currentTarget != null)
            ChaseTarget();
        else
            Patrol();

        // Update animation state (walk <-> run)
        UpdateAnimation();

        // Keep the stalker grounded
        Vector3 rayOrigin = transform.position + Vector3.up * 0.5f;
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, groundCheckDistance, groundMask))
        {
            float targetY = hit.point.y;
            Vector3 groundedPos = new Vector3(transform.position.x, targetY, transform.position.z);
            transform.position = Vector3.Lerp(transform.position, groundedPos, Mathf.Clamp01(Time.deltaTime * groundSnapSpeed));
        }
    }

    void UpdateAnimation()
    {
        if (animator == null || string.IsNullOrEmpty(runParamName)) return;

        // Set run boolean to match chasing state -> transitions should be defined in Animator
        animator.SetBool(runParamName, isChasing);
    }

    void UpdateTarget()
    {
        var candidates = GameObject.FindGameObjectsWithTag(targetTag);
        Transform nearest = null;
        float nearestSqr = Mathf.Infinity;

        for (int i = 0; i < candidates.Length; i++)
        {
            var t = candidates[i].transform;
            float dsq = (t.position - transform.position).sqrMagnitude;
            if (dsq < nearestSqr)
            {
                nearestSqr = dsq;
                nearest = t;
            }
        }

        float detectSqr = detectionRadius * detectionRadius;
        float loseSqr = loseRadius * loseRadius;

        if (nearest != null)
        {
            if (isChasing)
            {
                if (nearestSqr > loseSqr)
                {
                    isChasing = false;
                    currentTarget = null;
                }
                else
                {
                    currentTarget = nearest;
                }
            }
            else
            {
                if (nearestSqr <= detectSqr)
                {
                    isChasing = true;
                    currentTarget = nearest;
                }
            }
        }
        else
        {
            isChasing = false;
            currentTarget = null;
        }
    }

    void ChaseTarget()
    {
        Vector3 direction = currentTarget.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f) return;

        Quaternion targetRot = Quaternion.LookRotation(direction.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);

        float moveSpeed = speed * chaseSpeedMultiplier;
        transform.position += transform.forward * moveSpeed * Time.deltaTime;
    }

    void Patrol()
    {
        if (patrolPoints == null || patrolPoints.Length == 0) return;

        Transform waypoint = patrolPoints[patrolIndex];
        Vector3 dir = waypoint.position - transform.position;
        dir.y = 0f;

        if (dir.sqrMagnitude <= waypointTolerance * waypointTolerance)
        {
            // advance waypoint
            patrolIndex++;
            if (patrolIndex >= patrolPoints.Length)
            {
                if (patrolLoop) patrolIndex = 0;
                else patrolIndex = patrolPoints.Length - 1; // stay at last
            }
            return;
        }

        Quaternion targetRot = Quaternion.LookRotation(dir.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);

        // patrol uses base `speed`
        transform.position += transform.forward * speed * Time.deltaTime;
    }
}
