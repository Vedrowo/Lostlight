using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class StalkerMove : MonoBehaviour
{
    [Header("Movement")]
    public float speed = 3.5f;

    [Header("Detection")]
    public string targetTag = "Player";
    public float detectionRadius = 40f;
    public bool constantlySearchForTarget = true;

    [Header("Vision")]
    public float fieldOfViewAngle = 90f;
    public float eyeHeight = 1.6f;
    public LayerMask obstacleMask;

    [Header("Hearing")]
    public float hearingRadius = 20f;
    public float loudHearingMultiplier = 2f;

    [Header("Memory")]
    public float chasePersistence = 2.5f;

    Vector3 lastSeenPosition;
    float lastSeenTime;

    [Header("Search")]
    [Tooltip("When player is lost, pick random points within this radius around last seen position.")]
    public float searchRadius = 6f;
    public float searchPointInterval = 1.2f;

    [Header("Patrol (dynamic around player)")]
    [Tooltip("All global candidate patrol points.")]
    public Transform[] patrolPoints;
    [Tooltip("When patrolling, only use patrol points within this radius of the player.")]
    public float patrolRadiusAroundPlayer = 30f;
    public float waypointTolerance = 0.5f;

    [Header("Chase")]
    public float chaseSpeedMultiplier = 1.5f;
    public float attackCooldown = 0.8f;

    [Header("Attack Tuning")]
    public float attackStartRange = 1.6f;
    public float attackRangeBuffered = 2.5f;
    public float attackLockDuration = 0.45f;
    public float attackSnapSpeed = 14f;

    [Header("Animator")]
    public Animator animator;
    public string walkParamName = "isWalking";
    public string runParamName = "isRunning";
    public string attackBoolName = "isAttacking";
    public string triggeredBoolName = "isTriggered";

    [Header("Damage")]
    public bool killOnAttack = true;

    [Header("Activation")]
    [Tooltip("If true, the stalker will only search for targets after Activate() is called.")]
    public bool requireActivation = true;
    public bool activated = false;

    [Header("Flashlight")]
    public Light flashlightSpotlight;

    [Header("Flashlight Stun")]
    public bool canBeStunned = true;
    public float stunBuildTime = 0.8f;
    public float stunDecayTime = 1.2f;
    public float stunDuration = 2.5f;
    public float stunAngle = 25f;
    public float stunMaxDistance = 20f;
    public float stunDurationRequired = 0.8f;

    float stunTimer = 0f;
    bool isStunned = false;
    float stunEndTime = 0f;

    [Header("Player-activation options")]
    [Tooltip("If true the stalker will auto-activate when the player first sees it and then looks away.")]
    public bool autoActivateByPlayerSight = true;
    [Tooltip("If the player gets this close the stalker will activate immediately.")]
    public float activationProximity = 3f;
    [Tooltip("Half-angle (degrees) for player's look-to-see test.")]
    public float playerSeeAngle = 40f;
    [Tooltip("LayerMask used when testing line-of-sight from player/camera to the stalker. If zero, uses default Raycast (no mask).")]
    public LayerMask playerVisionMask;

    [Header("Player-activation timing (debounce)")]
    [Tooltip("How long the player must continuously see the stalker before it counts as 'seen'.")]
    public float requiredSightTime = 0.35f;
    [Tooltip("How long the player must continuously look away after seeing to trigger activation.")]
    public float requiredLookAwayTime = 0.18f;

    [Header("Activation distance")]
    [Tooltip("Maximum distance at which the 'saw then look away' activation is considered.")]
    public float activationMaxDistance = 100f;

    [Header("Unactivated behavior")]
    [Tooltip("If true the stalker will still patrol near player even when not activated.")]
    public bool patrolWhenUnactivated = false;

    [Header("Activation Prompt")]
    [Tooltip("UI Text object that shows when the stalker activates. Will auto-hide after a few seconds.")]
    public GameObject activationPromptUI;
    [Tooltip("How long the prompt stays on screen.")]
    public float promptDuration = 3f;

    [Header("Audio")]
    public AudioSource audioSource;
    public AudioSource scream;
    public AudioClip[] footstepSounds;
    public float stepInterval = 0.5f;
    [Range(0f, 1f)] public float footstepVolume = 0.8f;

    NavMeshAgent agent;
    bool isChasing;
    bool isAttacking = false;
    Transform currentTarget;
    float lastAttackTime;
    private GameState previousState;
    float searchTimer;
    bool isSearching;
    bool hasBeenSeenByPlayer = false;
    float playerSeeTimer = 0f;
    float playerLookAwayTimer = 0f;
    private float stepTimer;
    float activationTime;

    int currentDynamicPatrolIndex = -1;
    List<int> currentNearbyPatrolIndices = new List<int>();

    Transform playerTransformCached;

    void Awake()
    {
        var playerObj = GameObject.FindGameObjectWithTag(targetTag);
        if (playerObj != null) playerTransformCached = playerObj.transform;
    }

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        if (agent == null)
            agent = gameObject.AddComponent<NavMeshAgent>();

        agent.speed = speed;
        agent.acceleration = 8f;
        agent.angularSpeed = 300f;
        agent.stoppingDistance = 1f;
        agent.updateRotation = true;

        currentDynamicPatrolIndex = -1;
        lastAttackTime = -999f;

        if (animator == null)
            animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>();
    }

    void Update()
    {
        if (playerTransformCached == null)
        {
            var pgo = GameObject.FindGameObjectWithTag(targetTag);
            if (pgo != null) playerTransformCached = pgo.transform;
        }

        Transform playerTransform = playerTransformCached;

        HandleFlashlightStun(playerTransform);

        // Activation logic
        if (!activated && requireActivation && playerTransform != null)
        {
            float playerDist = Vector3.Distance(transform.position, playerTransform.position);

            if (playerDist <= activationProximity)
            {
                Activate();
            }
            else if (autoActivateByPlayerSight)
            {
                Transform cam = Camera.main != null ? Camera.main.transform : playerTransform;
                Vector3 camPos = cam.position;
                Vector3 stalkerCenter = transform.position + Vector3.up * eyeHeight;
                Vector3 dirToStalker = stalkerCenter - camPos;
                float distToStalker = dirToStalker.magnitude;
                Vector3 dirNorm = dirToStalker.normalized;
                float angle = Vector3.Angle(cam.forward, dirNorm);

                bool playerFacingStalker = angle <= playerSeeAngle;
                bool playerCanSee = false;

                if (playerFacingStalker && playerDist <= activationMaxDistance)
                {
                    RaycastHit hit;
                    bool blocked = (int)playerVisionMask == 0
                        ? Physics.Raycast(camPos, dirNorm, out hit, distToStalker)
                        : Physics.Raycast(camPos, dirNorm, out hit, distToStalker, playerVisionMask);

                    playerCanSee = !blocked;
                }

                if (playerCanSee)
                {
                    playerSeeTimer += Time.deltaTime;
                    playerLookAwayTimer = 0f;
                    if (playerSeeTimer >= requiredSightTime)
                    {
                        hasBeenSeenByPlayer = true;
                    }
                }
                else
                {
                    playerSeeTimer = 0f;

                    if (hasBeenSeenByPlayer)
                    {
                        bool walkedOutOfRange = playerDist > activationMaxDistance;
                        bool lookedAway = !playerFacingStalker;

                        if (walkedOutOfRange || lookedAway)
                        {
                            playerLookAwayTimer += Time.deltaTime;
                            if (playerLookAwayTimer >= requiredLookAwayTime)
                            {
                                Activate();
                            }
                        }
                        else
                        {
                            playerLookAwayTimer = 0f;
                        }
                    }
                }
            }
        }

        if (!activated && requireActivation)
        {
            if (patrolWhenUnactivated)
                DoPatrolNearPlayer(playerTransform);
            else
            {
                if (agent != null)
                {
                    agent.isStopped = true;
                    agent.ResetPath();
                }
                currentDynamicPatrolIndex = -1;
            }

            UpdateAnimation();
            return;
        }

        if (isStunned)
        {
            UpdateAnimation();
            return;
        }

        if (constantlySearchForTarget)
            UpdateTarget();

        if (isChasing && currentTarget != null)
            DoChase();
        else if (isChasing)
            DoSearch();
        else
            DoPatrolNearPlayer(playerTransform);

        UpdateAnimation();
        HandleFootsteps();
    }

    public void Activate()
    {
        activated = true;
        activationTime = Time.time;
        hasBeenSeenByPlayer = true;

        if (agent != null)
            agent.isStopped = false;

        if (animator != null)
        {
            if (!string.IsNullOrEmpty(triggeredBoolName))
                animator.SetBool(triggeredBoolName, true);
        }

        if (activationPromptUI != null)
            StartCoroutine(ShowPrompt());
    }

    IEnumerator ShowPrompt()
    {
        activationPromptUI.SetActive(true);
        yield return new WaitForSeconds(promptDuration);
        activationPromptUI.SetActive(false);
    }

    void UpdateTarget()
    {
        if (playerTransformCached == null) return;
        if (Time.time < activationTime + 0.1f)
            return;

        Transform target = playerTransformCached;
        float dist = Vector3.Distance(transform.position, target.position);

        bool canSee = false;

        if (dist <= detectionRadius)
        {
            Vector3 eyePos = transform.position + Vector3.up * eyeHeight;
            Vector3 dir = (target.position + Vector3.up * eyeHeight - eyePos).normalized;
            float angle = Vector3.Angle(transform.forward, dir);

            if (angle < fieldOfViewAngle / 2f)
            {
                if (!Physics.Raycast(eyePos, dir, dist, obstacleMask))
                {
                    canSee = true;
                    lastSeenPosition = target.position;
                    lastSeenTime = Time.time;
                }
            }
        }

        float moveNoise = 0f;
        var pm = target.GetComponent<PlayerMovement>();
        if (pm != null)
            moveNoise = pm.NoiseLevel;
        else
        {
            var rb = target.GetComponent<Rigidbody>();
            if (rb != null)
                moveNoise = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z).magnitude;
        }

        float effectiveHearing = hearingRadius;
        if (moveNoise > 3f) effectiveHearing *= loudHearingMultiplier;
        if (pm != null && pm.IsCrouching) effectiveHearing *= 0.45f;

        bool canHear = dist <= effectiveHearing;

        if (canSee || canHear)
        {
            if (!isChasing)
            {
                
                Debug.Log("Stalker spotted the player and screamed!");
                scream.Play();
                
                GameState currentState = GameManager.Instance.GetState();
                if (currentState != GameState.Chased)
                {
                    previousState = currentState;
                    GameManager.Instance.SetState(GameState.Chased);
                }
            }
            isChasing = true;
            isSearching = false;
            currentTarget = target;
        }
        else if (isChasing && Time.time > lastSeenTime + chasePersistence)
        {
            if (!isSearching)
            {
                GameState currentState = GameManager.Instance.GetState();
                if (currentState != GameState.Chased)
                    GameManager.Instance.SetState(previousState);
            }
            currentTarget = null;
            isSearching = true;
            searchTimer = 0f;
            lastSeenTime = Time.time;
        }
    }

    void DoChase()
    {
        agent.speed = speed * chaseSpeedMultiplier;

        if (currentTarget != null)
        {
            agent.isStopped = false;

            Vector3 targetVelocity = Vector3.zero;
            var rb = currentTarget.GetComponent<Rigidbody>();
            if (rb != null)
                targetVelocity = rb.linearVelocity;
            else
            {
                var pm = currentTarget.GetComponent<PlayerMovement>();
                if (pm != null)
                    targetVelocity = pm.CurrentVelocity;
                else
                {
                    var controller = currentTarget.GetComponent<CharacterController>();
                    if (controller != null)
                        targetVelocity = controller.velocity;
                }
            }

            float dist = Vector3.Distance(transform.position, currentTarget.position);
            float predictionTime = Mathf.Clamp(dist / (agent.speed + targetVelocity.magnitude + 0.1f), 0.05f, 1.0f);
            Vector3 predictedPosition = currentTarget.position + targetVelocity * predictionTime;

            NavMeshHit hit;
            Vector3 navDestination = currentTarget.position;
            if (NavMesh.SamplePosition(predictedPosition, out hit, 2f, NavMesh.AllAreas))
                navDestination = hit.position;
            else if (NavMesh.SamplePosition(currentTarget.position, out hit, 2f, NavMesh.AllAreas))
                navDestination = hit.position;

            agent.SetDestination(navDestination);
            lastSeenPosition = currentTarget.position;
            lastSeenTime = Time.time;
        }
        else
        {
            agent.SetDestination(lastSeenPosition);
            if (!agent.pathPending && agent.remainingDistance <= 1f)
            {
                isChasing = false;
                isSearching = false;
            }
        }

        // attack
        if (currentTarget != null && Time.time >= lastAttackTime + attackCooldown && !isAttacking)
        {
            float dist = Vector3.Distance(transform.position, currentTarget.position);
            if (dist <= attackStartRange)
            {
                StartCoroutine(AttackRoutine(currentTarget));
                lastAttackTime = Time.time;
            }
        }
    }

    void DoSearch()
    {
        agent.speed = speed;

        searchTimer += Time.deltaTime;
        if (searchTimer >= searchPointInterval || !agent.hasPath)
        {
            searchTimer = 0f;
            for (int i = 0; i < 10; i++)
            {
                Vector3 rand = lastSeenPosition + Random.insideUnitSphere * searchRadius;
                NavMeshHit hit;
                if (NavMesh.SamplePosition(rand, out hit, 4f, NavMesh.AllAreas))
                {
                    agent.SetDestination(hit.position);
                    break;
                }
            }
        }

        if (Time.time > lastSeenTime + chasePersistence)
        {
            if (isChasing || isSearching)
                GameManager.Instance.SetState(previousState);

            isChasing = false;
            isSearching = false;
            currentTarget = null;
            agent.ResetPath();
        }
    }

    IEnumerator AttackRoutine(Transform target)
    {
        if (target == null || isStunned) yield break;

        isAttacking = true;
        agent.isStopped = true;

        if (animator != null)
            animator.SetBool(attackBoolName, true);

        float timer = 0f;
        bool hitLanded = false;

        while (timer < attackLockDuration)
        {
            if (target == null) { isAttacking = false; yield break; }

            Vector3 dir = target.position - transform.position;
            dir.y = 0;
            if (dir.magnitude > 0.1f)
                agent.Move(dir.normalized * attackSnapSpeed * Time.deltaTime);

            float nowDist = Vector3.Distance(transform.position, target.position);
            if (nowDist <= attackStartRange)
            {
                if (killOnAttack) TryKillTarget(target);
                hitLanded = true;
                break;
            }

            timer += Time.deltaTime;
            yield return null;
        }

        if (!hitLanded)
        {
            float finalDist = Vector3.Distance(transform.position, target.position);
            if (finalDist <= attackRangeBuffered + 0.5f)
            {
                if (killOnAttack) TryKillTarget(target);
            }
        }

        agent.isStopped = false;

        if (animator != null)
            animator.SetBool(attackBoolName, false);

        isAttacking = false;
    }

    void TryKillTarget(Transform target)
    {
        if (target == null) return;

        var state = GameManager.Instance.GetState();
        if (state == GameState.Caught ||
            state == GameState.Dragging ||
            state == GameState.GettingUp ||
            state == GameState.Escaped)
            return;

        var ph = target.GetComponent<PlayerHealth>()
              ?? target.GetComponentInChildren<PlayerHealth>()
              ?? target.root.GetComponent<PlayerHealth>();

        if (ph == null)
        {
            var playerGO = GameObject.FindGameObjectWithTag(targetTag);
            if (playerGO != null)
                ph = playerGO.GetComponent<PlayerHealth>() ?? playerGO.GetComponentInChildren<PlayerHealth>();
        }

        if (ph == null) return;

        if (!GameManager.Instance.hasBeenCaught)
        {
            GameManager.Instance.hasBeenCaught = true;
            GameManager.Instance.SetState(GameState.Caught);
            CaptureSequence.Trigger(target, transform);
            return;
        }

        ph.TakeDamage(1);
    }

    public void ReturnToPatrol(Vector3 worldPosition)
    {
        if (agent != null)
        {
            agent.Warp(worldPosition);
            agent.ResetPath();
            agent.isStopped = false;
        }
        else
        {
            transform.position = worldPosition;
        }

        currentDynamicPatrolIndex = -1;
        activated = true;
        isChasing = false;
        isSearching = false;
        isAttacking = false;
        currentTarget = null;
        hasBeenSeenByPlayer = false;
        playerSeeTimer = 0f;
        playerLookAwayTimer = 0f;
        killOnAttack = true;

        if (animator != null)
        {
            animator.SetBool(attackBoolName, false);
            animator.SetBool(runParamName, false);
            animator.SetBool(walkParamName, true); // back to walking on patrol
        }

        Debug.Log($"Stalker '{gameObject.name}' returned to patrol at {worldPosition}");
    }

    void DoPatrolNearPlayer(Transform playerTransform)
    {
        agent.speed = speed;

        if (patrolPoints == null || patrolPoints.Length == 0)
        {
            agent.ResetPath();
            return;
        }

        if (currentDynamicPatrolIndex >= 0 && playerTransform != null)
        {
            float distFromPlayer = Vector3.Distance(playerTransform.position, patrolPoints[currentDynamicPatrolIndex].position);
            if (distFromPlayer > patrolRadiusAroundPlayer)
            {
                currentDynamicPatrolIndex = -1;
                agent.ResetPath();
            }
        }

        currentNearbyPatrolIndices.Clear();
        if (playerTransform != null)
        {
            for (int i = 0; i < patrolPoints.Length; i++)
            {
                if (patrolPoints[i] == null) continue;
                float d = Vector3.Distance(playerTransform.position, patrolPoints[i].position);
                if (d <= patrolRadiusAroundPlayer)
                    currentNearbyPatrolIndices.Add(i);
            }
        }

        if (currentNearbyPatrolIndices.Count == 0)
        {
            for (int i = 0; i < patrolPoints.Length; i++)
                if (patrolPoints[i] != null)
                    currentNearbyPatrolIndices.Add(i);
        }

        if (currentDynamicPatrolIndex < 0 && currentNearbyPatrolIndices.Count > 0)
        {
            currentDynamicPatrolIndex = currentNearbyPatrolIndices[Random.Range(0, currentNearbyPatrolIndices.Count)];
            agent.SetDestination(patrolPoints[currentDynamicPatrolIndex].position);
            return;
        }

        if (!agent.hasPath)
        {
            if (currentNearbyPatrolIndices.Count > 0)
            {
                currentDynamicPatrolIndex = currentNearbyPatrolIndices[Random.Range(0, currentNearbyPatrolIndices.Count)];
                agent.SetDestination(patrolPoints[currentDynamicPatrolIndex].position);
            }
            return;
        }

        if (!agent.pathPending && agent.remainingDistance <= waypointTolerance)
        {
            int nextIndex = currentDynamicPatrolIndex;
            if (currentNearbyPatrolIndices.Count > 1)
            {
                int attempts = 0;
                while (nextIndex == currentDynamicPatrolIndex && attempts < 6)
                {
                    nextIndex = currentNearbyPatrolIndices[Random.Range(0, currentNearbyPatrolIndices.Count)];
                    attempts++;
                }
            }
            currentDynamicPatrolIndex = nextIndex;
            agent.SetDestination(patrolPoints[currentDynamicPatrolIndex].position);
        }
    }

    void UpdateAnimation()
    {
        if (animator == null) return;

        animator.SetBool("isStunned", isStunned);

        if (isStunned)
        {
            if (!string.IsNullOrEmpty(walkParamName)) animator.SetBool(walkParamName, false);
            if (!string.IsNullOrEmpty(runParamName)) animator.SetBool(runParamName, false);
            if (!string.IsNullOrEmpty(attackBoolName)) animator.SetBool(attackBoolName, false);
            return;
        }

        bool isMoving = agent.velocity.magnitude > 0.1f && !agent.isStopped;

        if (isChasing)
        {
            // chasing — run, don't walk
            if (!string.IsNullOrEmpty(walkParamName)) animator.SetBool(walkParamName, false);
            if (!string.IsNullOrEmpty(runParamName)) animator.SetBool(runParamName, isMoving);
        }
        else
        {
            // patrolling / searching — walk, don't run
            if (!string.IsNullOrEmpty(runParamName)) animator.SetBool(runParamName, false);
            if (!string.IsNullOrEmpty(walkParamName)) animator.SetBool(walkParamName, isMoving);
        }
    }

    void HandleFootsteps()
    {
        bool isMoving = agent.velocity.magnitude > 0.1f && agent.remainingDistance > agent.stoppingDistance;

        if (!isMoving)
        {
            stepTimer = 0f;
            return;
        }

        float currentStepInterval = stepInterval;
        if (isChasing)
            currentStepInterval *= 0.7f;

        stepTimer += Time.deltaTime;

        if (stepTimer >= currentStepInterval)
        {
            if (footstepSounds.Length > 0 && audioSource != null)
            {
                AudioClip clip = footstepSounds[Random.Range(0, footstepSounds.Length)];
                audioSource.pitch = isChasing ? 1.2f : 1.0f;
                audioSource.PlayOneShot(clip, footstepVolume);
            }

            stepTimer = 0f;
        }
    }

    void HandleFlashlightStun(Transform playerTransform)
    {
        if (!canBeStunned || playerTransform == null || flashlightSpotlight == null) return;

        if (isStunned)
        {
            if (Time.time >= stunEndTime)
            {
                isStunned = false;
                stunTimer = 0f;

                isChasing = false;
                isSearching = false;
                isAttacking = false;
                currentTarget = null;
                lastSeenTime = -999f;

                if (agent != null)
                    agent.isStopped = false;

                lastAttackTime = Time.time + 1.5f;

                if (animator != null)
                {
                    animator.SetBool("isStunned", false);
                    animator.SetBool(runParamName, false);
                    animator.SetBool(walkParamName, true); // resume walking after stun
                }

                GameManager.Instance.SetState(previousState);
                Debug.Log("Stalker recovered from stun.");
            }
            return;
        }

        if (flashlightSpotlight.enabled)
        {
            Vector3 lightOrigin = flashlightSpotlight.transform.position;
            Vector3 stalkerTarget = transform.position + Vector3.up * eyeHeight;
            Vector3 directionToStalker = (stalkerTarget - lightOrigin).normalized;
            float distance = Vector3.Distance(lightOrigin, stalkerTarget);
            float angleToStalker = Vector3.Angle(flashlightSpotlight.transform.forward, directionToStalker);

            if (angleToStalker < flashlightSpotlight.spotAngle / 2f && distance <= stunMaxDistance)
            {
                if (!Physics.Raycast(lightOrigin, directionToStalker, distance, obstacleMask))
                {
                    stunTimer += Time.deltaTime;

                    if (stunTimer >= stunDurationRequired)
                    {
                        TriggerStun();
                        stunTimer = 0f;
                    }
                    return;
                }
            }
        }

        if (stunTimer > 0)
        {
            stunTimer -= Time.deltaTime * stunDecayTime;
            stunTimer = Mathf.Max(stunTimer, 0);
        }
    }

    void TriggerStun()
    {
        isStunned = true;
        stunEndTime = Time.time + stunDuration;

        StopAllCoroutines();

        isChasing = false;
        isSearching = false;
        isAttacking = false;
        currentTarget = null;
        lastSeenTime = -999f;

        if (agent != null)
        {
            agent.isStopped = true;
            agent.ResetPath();
            agent.velocity = Vector3.zero;
        }

        if (animator != null)
        {
            animator.SetBool(attackBoolName, false);
            animator.SetBool(runParamName, false);
            animator.SetBool(walkParamName, false);
            animator.SetTrigger("stunned");
        }
    }
}