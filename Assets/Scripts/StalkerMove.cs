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
    [Tooltip("All global candidate patrol points (place these on your road).")]
    public Transform[] patrolPoints;
    [Tooltip("When patrolling, only use patrol points within this radius of the player.")]
    public float patrolRadiusAroundPlayer = 30f;
    public bool patrolLoop = true;
    public float waypointTolerance = 0.5f;

    [Header("Chase")]
    public float chaseSpeedMultiplier = 1.5f;
    public float attackCooldown = 0.8f;

    [Header("Attack Tuning")]
    public float attackStartRange = 1.6f;
    public float attackRangeBuffered = 2.5f;   // increased (more forgiving)
    public float attackLockDuration = 0.45f;   // slightly longer lock
    public float attackSnapSpeed = 14f;        // stronger pull

    [Header("Animator")]
    public Animator animator;
    public string runParamName = "isRunning";
    public string attackTriggerName = "Attack";
    public string attackBoolName = "isAttacking";
    public string triggeredBoolName = "isTriggered";

    [Header("Damage")]
    public bool killOnAttack = true;

    // Activation: level/trigger can call Activate() to start the stalker moving
    [Header("Activation")]
    [Tooltip("If true, the stalker will only search for targets after Activate() is called.")]
    public bool requireActivation = true;
    bool activated = false;

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

    // NEW: control whether unactivated stalker should still patrol (default = false)
    [Header("Unactivated behavior")]
    [Tooltip("If true the stalker will still patrol near player even when not activated.")]
    public bool patrolWhenUnactivated = false;

    // NEW: display distance on screen
    [Header("Debug / HUD")]
    [Tooltip("If true, draws the distance from the stalker to the player on-screen.")]
    public bool displayDistance = true;
    [Tooltip("Screen position (pixels) where the distance text will be drawn.")]
    public Vector2 distanceScreenPos = new Vector2(10f, 10f);
    [Tooltip("Font size for the distance text.")]
    public int distanceFontSize = 18;
    [Tooltip("Color for the distance text.")]
    public Color distanceColor = Color.white;

    // NEW: verbose activation debug
    [Tooltip("Enable to print activation/visibility debug info to the Console and draw the sight ray.")]
    public bool verboseActivationDebug = false;

    NavMeshAgent agent;
    int patrolIndex;
    bool isChasing;
    Transform currentTarget;
    float lastAttackTime;

    // search internals
    float searchTimer;
    Vector3 currentSearchPoint;
    bool isSearching;

    // activation internals
    bool hasBeenSeenByPlayer = false;
    bool lastPlayerCanSee = false;
    float playerSeeTimer = 0f;
    float playerLookAwayTimer = 0f;

    // dynamic patrol internals
    int currentDynamicPatrolIndex = -1;
    List<int> currentNearbyPatrolIndices = new List<int>();

    // cache player transform for distance display
    Transform playerTransformCached;
    GUIStyle cachedDistanceStyle;

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

        patrolIndex = 0;
        currentDynamicPatrolIndex = -1;
        lastAttackTime = -999f;

        if (animator == null)
            animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>();

        var playerObj = GameObject.FindGameObjectWithTag(targetTag);
        if (playerObj != null) playerTransformCached = playerObj.transform;
    }

    void Update()
    {
        // refresh cached player transform if needed
        if (playerTransformCached == null)
        {
            var pgo = GameObject.FindGameObjectWithTag(targetTag);
            if (pgo != null) playerTransformCached = pgo.transform;
        }

        // find player once per frame if needed
        GameObject playerObj = GameObject.FindGameObjectWithTag(targetTag);
        Transform playerTransform = playerObj != null ? playerObj.transform : null;

        // Activation logic (auto by player sight or proximity) with distance gating
        if (!activated && requireActivation && playerTransform != null)
        {
            bool playerCanSee = false;

            // compute player distance early
            float playerDist = Vector3.Distance(transform.position, playerTransform.position);

            // proximity activation immediate
            if (playerDist <= activationProximity)
            {
                Activate();
            }
            else
            {
                // only consider the look-away activation if player is within activationMaxDistance
                if (playerDist <= activationMaxDistance && autoActivateByPlayerSight)
                {
                    // try camera first, fallback to player transform forward if no camera
                    Transform cam = Camera.main != null ? Camera.main.transform : playerTransform;
                    Vector3 camPos = cam.position;
                    Vector3 stalkerCenter = transform.position + Vector3.up * eyeHeight;
                    Vector3 dirToStalker = (stalkerCenter - camPos);
                    float distToStalker = dirToStalker.magnitude;
                    Vector3 dirNorm = dirToStalker.normalized;
                    float angle = Vector3.Angle(cam.forward, dirNorm);

                    if (angle <= playerSeeAngle)
                    {
                        bool blocked = false;
                        RaycastHit hit;
                        if ((int)playerVisionMask == 0)
                        {
                            blocked = Physics.Raycast(camPos, dirNorm, out hit, distToStalker);
                        }
                        else
                        {
                            blocked = Physics.Raycast(camPos, dirNorm, out hit, distToStalker, playerVisionMask);
                        }

                        playerCanSee = !blocked;

                        if (verboseActivationDebug)
                        {
                            // draw debug ray and log on state change
                            Debug.DrawRay(camPos, dirNorm * distToStalker, playerCanSee ? Color.green : Color.red, 0.15f);
                            if (playerCanSee != lastPlayerCanSee)
                            {
                                string hitName = hit.collider != null ? hit.collider.name : "none";
                                Debug.Log($"[StalkerDebug] '{gameObject.name}': playerCanSee={playerCanSee} dist={playerDist:F1} angle={angle:F1} blocked={blocked} hit={hitName}");
                            }
                        }
                    }

                    // sight debounce: require continuous sight for requiredSightTime to count as "seen"
                    if (playerCanSee)
                    {
                        playerSeeTimer += Time.deltaTime;
                        playerLookAwayTimer = 0f;
                        if (playerSeeTimer >= requiredSightTime)
                        {
                            if (!hasBeenSeenByPlayer)
                                hasBeenSeenByPlayer = true;
                            lastPlayerCanSee = true;

                            if (verboseActivationDebug)
                                Debug.Log($"[StalkerDebug] '{gameObject.name}' registered continuous sight (timer={playerSeeTimer:F2})");
                        }
                    }
                    else
                    {
                        playerSeeTimer = 0f;
                        if (lastPlayerCanSee || hasBeenSeenByPlayer)
                        {
                            playerLookAwayTimer += Time.deltaTime;
                            if (playerLookAwayTimer >= requiredLookAwayTime && hasBeenSeenByPlayer)
                            {
                                // player looked away long enough after seeing -> activate
                                if (verboseActivationDebug)
                                    Debug.Log($"[StalkerDebug] '{gameObject.name}' look-away timer reached ({playerLookAwayTimer:F2}) -> Activate()");
                                Activate();
                            }
                        }
                        lastPlayerCanSee = false;
                    }
                }
                else
                {
                    // player too far: reset look timers so we don't trigger because of stale state
                    playerSeeTimer = 0f;
                    playerLookAwayTimer = 0f;
                    lastPlayerCanSee = false;
                }
            }
        }

        // if activation is required and not activated, optionally patrol or remain idle
        if (!activated && requireActivation)
        {
            if (patrolWhenUnactivated)
            {
                DoPatrolNearPlayer(playerTransform);
            }
            else
            {
                // ENSURE IDLE: stop agent and clear any path so stalker won't wander
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

        if (constantlySearchForTarget)
            UpdateTarget();

        if (isChasing && currentTarget != null)
            DoChase();
        else if (isChasing) // lost target but still searching
            DoSearch();
        else
            DoPatrolNearPlayer(playerTransform);

        UpdateAnimation();
    }

    // call from level script / camera when player "sees" the stalker (external hook)
    public void Activate()
    {
        activated = true;
        hasBeenSeenByPlayer = true;
        // make sure agent resumes movement when activated
        if (agent != null)
            agent.isStopped = false;

        // Set the animator boolean you added so Animator transitions out of idle.
        if (animator != null && !string.IsNullOrEmpty(triggeredBoolName))
            animator.SetBool(triggeredBoolName, true);

        Debug.Log($"StalkerMove.Activate() called on '{gameObject.name}'");
        // optionally start moving immediately towards player last seen position
    }

    void UpdateTarget()
    {
        GameObject player = GameObject.FindGameObjectWithTag(targetTag);
        if (player == null) return;

        Transform target = player.transform;
        float dist = Vector3.Distance(transform.position, target.position);

        bool canSee = false;

        // VISION
        if (dist <= detectionRadius)
        {
            Vector3 eyePos = transform.position + Vector3.up * eyeHeight;
            Vector3 dir = (target.position + Vector3.up * eyeHeight - eyePos).normalized;
            float angle = Vector3.Angle(transform.forward, dir);

            if (angle < fieldOfViewAngle / 2f)
            {
                // raycast against obstacles; obstacleMask should include world geometry colliders (meshes, capsules, etc.)
                if (!Physics.Raycast(eyePos, dir, dist, obstacleMask))
                {
                    canSee = true;
                    lastSeenPosition = target.position;
                    lastSeenTime = Time.time;
                }
            }
        }

        // HEARING
        // Prefer PlayerMovement noise if available
        float moveNoise = 0f;
        var pm = player.GetComponent<PlayerMovement>();
        if (pm != null)
        {
            moveNoise = pm.NoiseLevel;
        }
        else
        {
            var rb = player.GetComponent<Rigidbody>();
            if (rb != null)
                moveNoise = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z).magnitude;
        }   

        float effectiveHearing = hearingRadius;

        // if player is loud (running) amplify hearing radius
        if (moveNoise > 3f)
            effectiveHearing *= loudHearingMultiplier;

        // if player is crouching, reduce hearing sensitivity
        if (pm != null && pm.IsCrouching)
            effectiveHearing *= 0.45f;

        bool canHear = dist <= effectiveHearing;

        if (canSee || canHear)
        {
            isChasing = true;
            isSearching = false;
            currentTarget = target;
        }
        else if (isChasing && Time.time > lastSeenTime + chasePersistence)
        {
            // lost target, start searching near last seen position
            currentTarget = null;
            isSearching = true;
            searchTimer = 0f;
            currentSearchPoint = lastSeenPosition;
            lastSeenTime = Time.time;
        }
    }

    void DoChase()
    {
        agent.speed = speed * chaseSpeedMultiplier;

        if (currentTarget != null)
        {
            agent.isStopped = false;

            // Prediction: prefer Rigidbody, then PlayerMovement.CurrentVelocity, then CharacterController
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

            // dynamic prediction time: closer -> smaller prediction, farther -> more prediction but clamp
            float predictionTime = Mathf.Clamp(dist / (agent.speed + targetVelocity.magnitude + 0.1f), 0.05f, 1.0f);

            Vector3 predictedPosition = currentTarget.position + targetVelocity * predictionTime;

            // ensure predicted point is on the NavMesh; fallback to current target position if not
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
            // this path should be handled by DoSearch; keep fallback to last position
            agent.SetDestination(lastSeenPosition);

            if (!agent.pathPending && agent.remainingDistance <= 1f)
            {
                isChasing = false;
                isSearching = false;
                return;
            }
        }

        // ATTACK TRIGGER
        if (currentTarget != null && Time.time >= lastAttackTime + attackCooldown)
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

        // pick a new random search point every interval
        searchTimer += Time.deltaTime;
        if (searchTimer >= searchPointInterval || !agent.hasPath)
        {
            searchTimer = 0f;

            // sample a random point on NavMesh around lastSeenPosition
            for (int i = 0; i < 10; i++)
            {
                Vector3 rand = lastSeenPosition + Random.insideUnitSphere * searchRadius;
                NavMeshHit hit;
                if (NavMesh.SamplePosition(rand, out hit, 4f, NavMesh.AllAreas))
                {
                    currentSearchPoint = hit.position;
                    agent.SetDestination(currentSearchPoint);
                    break;
                }
            }
        }

        // stop searching after chasePersistence elapsed since last seen
        if (Time.time > lastSeenTime + chasePersistence)
        {
            isChasing = false;
            isSearching = false;
            currentTarget = null;
            agent.ResetPath();
        }
    }

    IEnumerator AttackRoutine(Transform target)
    {
        if (target == null) yield break;

        agent.isStopped = true; // stop navmesh fighting

        if (animator != null)
        {
            animator.SetTrigger(attackTriggerName);
            animator.SetBool(attackBoolName, true);
        }

        float timer = 0f;

        while (timer < attackLockDuration)
        {
            if (target == null) yield break;

            Vector3 dir = (target.position - transform.position);
            dir.y = 0;

            if (dir.magnitude > 0.1f)
            {
                Vector3 move = dir.normalized * attackSnapSpeed * Time.deltaTime;
                agent.Move(move);
            }

            // immediate-hit check while locked: if close enough, apply kill immediately
            float nowDist = Vector3.Distance(transform.position, target.position);
            if (nowDist <= attackStartRange)
            {
                if (killOnAttack)
                    TryKillTarget(target);
                break;
            }

            timer += Time.deltaTime;
            yield return null;
        }

        // FINAL HIT (VERY forgiving) - fallback if the immediate check didn't kill
        float finalDist = Vector3.Distance(transform.position, target.position);

        if (finalDist <= attackRangeBuffered + 0.5f)
        {
            if (killOnAttack)
                TryKillTarget(target);
        }

        agent.isStopped = false;

        if (animator != null)
            animator.SetBool(attackBoolName, false);
    }

    void TryKillTarget(Transform target)
    {
        if (target == null) return;
        if (GameManager.Instance.GetState() != GameState.Exploration &&
        GameManager.Instance.GetState() != GameState.EscapeSequence)
            return; // don't kill player outside normal gameplay

        var ph = target.GetComponent<PlayerHealth>() ??
                 target.GetComponentInChildren<PlayerHealth>() ??
                 target.root.GetComponent<PlayerHealth>();

        if (ph == null) return;

        // FIRST TIME capture
        if (!GameManager.Instance.hasBeenCaught)
        {
            GameManager.Instance.hasBeenCaught = true;
            GameManager.Instance.SetState(GameState.Caught);

            // Start the capture cinematic / sequence that performs hit, blackout, staged dragging and transition to EscapeSequence.
            CaptureSequence.Trigger(target, transform);
            return;
        }
        Debug.Log("[Stalker] Attempting to kill player...");
        // AFTER THAT normal death
        ph.Die();
        Debug.Log("[Stalker] Player.Die() called successfully.");
    }

    // Public helper to teleport the stalker to a new position and reset it to normal patrol state.
    // Use NavMeshAgent.Warp so NavMesh stays consistent.
    public void ReturnToPatrol(Vector3 worldPosition)
    {
        if (agent != null)
        {
            // warp to destination so navmesh doesn't try to path-find through the teleport gap
            agent.Warp(worldPosition);
            agent.ResetPath();
            agent.isStopped = false;
        }
        else
        {
            transform.position = worldPosition;
        }

        // reset dynamic patrol selection so it will pick appropriate nearby point next Update
        currentDynamicPatrolIndex = -1;

        // make sure the stalker resumes normal patrol/chase logic immediately
        activated = true;
        isChasing = false;
        isSearching = false;
        currentTarget = null;
        hasBeenSeenByPlayer = false;
        lastPlayerCanSee = false;
        playerSeeTimer = 0f;
        playerLookAwayTimer = 0f;
        // In ReturnToPatrol or after the escape sequence, make sure killOnAttack = true
        killOnAttack = true;

        // clear animator attack state
        if (animator != null)
        {
            animator.SetBool(attackBoolName, false);
            // do not forcibly clear triggeredBoolName if you want the stalker to keep its "triggered" personality;
            // if you'd prefer it to return to idle animation, uncomment the next line:
            // if (!string.IsNullOrEmpty(triggeredBoolName)) animator.SetBool(triggeredBoolName, false);
        }

        Debug.Log($"Stalker '{gameObject.name}' returned to patrol at {worldPosition}");
    }

    // Patrolling around player's vicinity: select dynamic subset of patrolPoints within patrolRadiusAroundPlayer
    void DoPatrolNearPlayer(Transform playerTransform)
    {
        agent.speed = speed;

        if (patrolPoints == null || patrolPoints.Length == 0)
        {
            agent.ResetPath();
            return;
        }

        // build nearby list if player available, otherwise fallback to all points
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

        // fallback to all points if none are near player
        if (currentNearbyPatrolIndices.Count == 0)
        {
            for (int i = 0; i < patrolPoints.Length; i++)
            {
                if (patrolPoints[i] != null)
                    currentNearbyPatrolIndices.Add(i);
            }
        }

        // select a current point if none selected
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
            // pick next random nearby point (avoid repeating same point immediately if possible)
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
        if (animator == null || string.IsNullOrEmpty(runParamName)) return;
        animator.SetBool(runParamName, isChasing && !agent.isStopped);
    }

    // Draw distance HUD
    void OnGUI()
    {
        if (!displayDistance) return;
        if (playerTransformCached == null)
        {
            var pgo = GameObject.FindGameObjectWithTag(targetTag);
            if (pgo == null) return;
            playerTransformCached = pgo.transform;
        }

        float dist = Vector3.Distance(transform.position, playerTransformCached.position);

        if (cachedDistanceStyle == null)
        {
            cachedDistanceStyle = new GUIStyle(GUI.skin.label);
            cachedDistanceStyle.fontSize = Mathf.Max(10, distanceFontSize);
            cachedDistanceStyle.normal.textColor = distanceColor;
        }

        string text = $"Stalker distance: {dist:F1} m";
        Rect r = new Rect(distanceScreenPos.x, distanceScreenPos.y, 400f, cachedDistanceStyle.fontSize + 6);
        GUI.Label(r, text, cachedDistanceStyle);
    }
}