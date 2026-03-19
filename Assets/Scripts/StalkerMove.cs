using UnityEngine;
using UnityEngine.AI;

public class StalkerMove : MonoBehaviour
{
    [Header("Movement")]
    public float speed = 3.5f;
    public float rotationSpeed = 5f; // used for Animator/visuals if needed

    [Header("Detection")]
    public string targetTag = "Player";
    public float detectionRadius = 40f;
    public float loseRadius = 50f;
    public bool constantlySearchForTarget = true;

    [Header("Patrol")]
    public Transform[] patrolPoints;
    public bool patrolLoop = true;
    public float waypointTolerance = 0.5f;

    [Header("Chase")]
        [Tooltip("Multiplier applied to `speed` while chasing")]
    public float chaseSpeedMultiplier = 1.5f;
    public float attackRange = 1.2f;
    public float attackCooldown = 0.8f;

    [Header("Animator")]
    public Animator animator;
    public string runParamName = "isRunning";
    public string attackTriggerName = "Attack";
    public string attackBoolName = "isAttacking";

    [Header("Damage")]
    [Tooltip("If true, the stalker will attempt to 'kill' the player by calling Die() on a PlayerHealth component when it lands an attack.")]
    public bool killOnAttack = true;

    [Header("Stuck recovery")]
    public float stuckVelocityThreshold = 0.05f;
    public float stuckCheckInterval = 0.6f;
    public float stuckResetDelay = 0.25f;

    NavMeshAgent agent;
    int patrolIndex;
    bool isChasing;
    Transform currentTarget;
    float lastAttackTime;
    float lastStuckCheck;
    Vector3 lastStuckPos;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        if (agent == null)
            agent = gameObject.AddComponent<NavMeshAgent>();

        // configure agent sensible defaults (tweak in inspector)
        agent.speed = speed;
        agent.acceleration = 8f;
        agent.angularSpeed = 120f;
        agent.stoppingDistance = attackRange;
        agent.autoBraking = true;
        agent.updateRotation = true;
        agent.updateUpAxis = true;

        patrolIndex = 0;
        lastAttackTime = -999f;
        lastStuckCheck = Time.time;
        lastStuckPos = transform.position;

        // optional: animator find
        if (animator == null)
            animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>();
    }

    void Update()
    {
        if (constantlySearchForTarget)
            UpdateTarget();

        if (isChasing && currentTarget != null)
            DoChase();
        else
            DoPatrol();

        UpdateAnimation();
        RunStuckCheck();
    }

    void UpdateTarget()
    {
        var candidates = GameObject.FindGameObjectsWithTag(targetTag);
        Transform nearest = null;
        float nearestSqr = Mathf.Infinity;
        Vector3 pos = transform.position;

        for (int i = 0; i < candidates.Length; i++)
        {
            Vector3 diff = candidates[i].transform.position - pos;
            diff.y = 0f;
            float dsq = diff.sqrMagnitude;
            if (dsq < nearestSqr)
            {
                nearestSqr = dsq;
                nearest = candidates[i].transform;
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

    void DoChase()
    {
        if (currentTarget == null) return;

        // update agent speed & destination
        agent.speed = speed * chaseSpeedMultiplier;
        agent.stoppingDistance = attackRange;

        // set destination every frame for moving targets
        agent.SetDestination(currentTarget.position);

        // if close enough, trigger attack
        float sqrDist = (currentTarget.position - transform.position).sqrMagnitude;
        if (sqrDist <= attackRange * attackRange && Time.time >= lastAttackTime + attackCooldown)
        {
            // stop and attack (agent will keep orientation by default)
            agent.isStopped = true;
            if (animator != null)
            {
                if (!string.IsNullOrEmpty(attackTriggerName))
                    animator.SetTrigger(attackTriggerName);
                if (!string.IsNullOrEmpty(attackBoolName))
                    animator.SetBool(attackBoolName, true);
            }

            // apply damage / kill
            if (killOnAttack)
                TryKillTarget(currentTarget);

            lastAttackTime = Time.time;
            Invoke(nameof(ResumeAgentAfterAttack), attackCooldown);
        }
        else
        {
            agent.isStopped = false;
        }
    }

    void TryKillTarget(Transform target)
    {
        if (target == null) return;

        // try to find PlayerHealth on the target or its root
        var ph = target.GetComponent<PlayerHealth>() ?? target.GetComponentInChildren<PlayerHealth>() ?? target.root.GetComponent<PlayerHealth>();
        if (ph != null)
        {
            ph.Die();
            return;
        }

        // fallback: if this is a generic GameObject with a Rigidbody, try to simulate a force or disable it
        var rb = target.GetComponent<Rigidbody>() ?? target.GetComponentInChildren<Rigidbody>();
        if (rb != null)
        {
            // disable movement by making kinematic and/or destroying common movement scripts
            rb.isKinematic = false;
            rb.AddForce((target.position - transform.position).normalized * 2f, ForceMode.Impulse);
        }
    }

    void ResumeAgentAfterAttack()
    {
        if (agent != null) agent.isStopped = false;
        if (animator != null && !string.IsNullOrEmpty(attackBoolName))
            animator.SetBool(attackBoolName, false);
    }

    void DoPatrol()
    {
        if (patrolPoints == null || patrolPoints.Length == 0)
        {
            agent.ResetPath();
            return;
        }

        agent.speed = speed;
        agent.stoppingDistance = 0.5f;
        agent.autoBraking = false;

        // If agent has no path, start moving to the current waypoint.
        if (!agent.hasPath)
        {
            agent.SetDestination(patrolPoints[patrolIndex].position);
            return;
        }

        // If agent has a path and is not waiting on path calculation, check arrival.
        if (!agent.pathPending && agent.remainingDistance <= waypointTolerance)
        {
            // Advance waypoint when we've reached the current one.
            patrolIndex++;
            if (patrolIndex >= patrolPoints.Length)
            {
                patrolIndex = patrolLoop ? 0 : patrolPoints.Length - 1;
            }

            // Set destination to the next waypoint.
            agent.SetDestination(patrolPoints[patrolIndex].position);
        }
    }


    void UpdateAnimation()
    {
        if (animator == null || string.IsNullOrEmpty(runParamName)) return;
        animator.SetBool(runParamName, isChasing && !agent.isStopped);
    }

    void RunStuckCheck()
    {
        if (Time.time < lastStuckCheck + stuckCheckInterval) return;

        float moved = (transform.position - lastStuckPos).magnitude;
        lastStuckCheck = Time.time;
        lastStuckPos = transform.position;

        // if agent has path, not moving enough and not close to destination, reset path to replan
        if (agent.hasPath && agent.velocity.sqrMagnitude < stuckVelocityThreshold * stuckVelocityThreshold && agent.remainingDistance > 0.5f)
        {
            // quick reset + small pause
            agent.ResetPath();
            Invoke(nameof(ResumeAfterStuckReset), stuckResetDelay);
        }
    }

    void ResumeAfterStuckReset()
    {
        // reassign destination after a short delay
        if (isChasing && currentTarget != null)
            agent.SetDestination(currentTarget.position);
        else if (patrolPoints != null && patrolPoints.Length > 0)
            agent.SetDestination(patrolPoints[patrolIndex].position);
    }
}
