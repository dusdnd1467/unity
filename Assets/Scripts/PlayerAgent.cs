using UnityEngine;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

/// <summary>
/// 미로 술래잡기 환경의 플레이어 에이전트 (강화학습):
/// 1. 열쇠(KeyItem)를 탐색해 획득 (+1.5 보상)
/// 2. 탈출문(EscapeDoor)을 찾아 탈출 (+2.5 보상)
/// 3. 적(EnemyAgent)의 추적을 회피 (체포 시 -1.0 보상)
/// 4. 상자(MovableBox)를 조작하여 적의 진로를 방해하거나 길을 확보
/// </summary>
public class PlayerAgent : MazeAgentBase
{
    [Header("상태")]
    public bool hasKey = false;

    [Header("상자 상호작용")]
    [Tooltip("적이 고정해 둔 상자 근처에 접근 시 자동으로 해제를 시도할지 여부")]
    public bool autoUnlockEnemyLockedBox = true;

    [Header("디버그 정보 (읽기 전용)")]
    [SerializeField] private float currentDistanceToGoal;
    [SerializeField] private float currentDistanceToEnemy;

    private float previousDistanceToGoal;
    private float previousDistanceToEnemy;

    public void InitializePlayer(MazeArea mazeArea)
    {
        InitializeAgent(mazeArea);
    }

    public override void OnEpisodeBegin()
    {
        ReleaseActiveBox();
        hasKey = false;

        if (area != null)
        {
            area.ResetArea();
        }
        else
        {
            ResetPosition();
        }
    }

    public override void ResetPosition()
    {
        base.ResetPosition();
        hasKey = false;

        UpdateDistances();
        previousDistanceToGoal = currentDistanceToGoal;
        previousDistanceToEnemy = currentDistanceToEnemy;
    }

    private void Update()
    {
        // Heuristic 수동 테스트 시 키보드 입력 감지 (F/O: 고정, E/U: 잡기/해제)
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return;

        if (kb.fKey.wasPressedThisFrame || kb.oKey.wasPressedThisFrame)
        {
            HandleBoxInteraction(2, SoundSource.Player, Time.deltaTime, false, onLockSuccess: () => AddReward(0.02f));
        }

        isHoldingInteractKey = kb.eKey.isPressed || kb.uKey.isPressed;
        if (isHoldingInteractKey)
        {
            HandleBoxInteraction(1, SoundSource.Player, Time.deltaTime, false, onUnlockSuccess: () => AddReward(0.05f));
        }
        else if (kb.eKey.wasReleasedThisFrame || kb.uKey.wasReleasedThisFrame)
        {
            ReleaseActiveBox();
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // 1. 열쇠 소지 여부 (1차원)
        sensor.AddObservation(hasKey ? 1.0f : 0.0f);

        // 2. 현재 목표(열쇠 또는 탈출문) 상대 벡터 (3차원)
        Vector3 goalRel = Vector3.zero;
        if (area != null)
        {
            Transform target = hasKey ? area.GetClosestDoor(transform.position) : area.GetClosestActiveKey(transform.position);
            if (target != null)
            {
                goalRel = (target.position - transform.position) * 0.05f;
            }
        }
        sensor.AddObservation(goalRel);

        // 3. 적 소리 상대 위치(3차원) + 남은 시간 비율(1차원) (총 4차원)
        if (area != null && area.EnemySoundTimer > 0f)
        {
            Vector3 soundRel = (area.EnemySoundPosition - transform.position) * 0.05f;
            sensor.AddObservation(soundRel);
            sensor.AddObservation(area.EnemySoundTimer / Mathf.Max(0.01f, area.soundDuration));
        }
        else
        {
            sensor.AddObservation(Vector3.zero);
            sensor.AddObservation(0f);
        }

        // 4. 플레이어 자신의 Rigidbody 속도 (2차원: X, Z)
        Vector2 speedNorm = Vector2.zero;
        if (rb != null && moveSpeed > 0f)
        {
            speedNorm = new Vector2(
                Mathf.Clamp(rb.linearVelocity.x / moveSpeed, -1f, 1f),
                Mathf.Clamp(rb.linearVelocity.z / moveSpeed, -1f, 1f));
        }
        sensor.AddObservation(speedNorm);

        // 5. 적(Enemy)의 상대 위치 벡터 (3차원)
        Vector3 enemyRel = Vector3.zero;
        if (area != null && area.enemy != null)
        {
            enemyRel = (area.enemy.transform.position - transform.position) * 0.05f;
        }
        sensor.AddObservation(enemyRel);

        // 총 VectorObservationSize = 1 + 3 + 4 + 2 + 3 = 13차원
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // 1. 상자 상호작용
        if (!isHoldingInteractKey)
        {
            int boxAction = actions.DiscreteActions.Length > 0 ? actions.DiscreteActions[0] : 0;
            HandleBoxInteraction(
                boxAction,
                SoundSource.Player,
                Time.fixedDeltaTime,
                autoUnlockEnemyLockedBox,
                onUnlockSuccess: () => AddReward(0.05f),
                onLockSuccess: () => AddReward(0.02f));
        }

        // 2. 물리 이동 (상자 해제 채널링 중에는 제자리 정지)
        float currentSpeed = IsChannelingBox() ? 0f : moveSpeed;
        Vector3 moveInput = new Vector3(actions.ContinuousActions[0], 0f, actions.ContinuousActions[1]);
        MoveAgent(moveInput, currentSpeed, Time.fixedDeltaTime);

        // 3. 거리 기반 보상 성형(Reward Shaping)
        UpdateDistances();

        // 3-1. 목표(열쇠/탈출문)로 접근 시 보상
        float goalDelta = previousDistanceToGoal - currentDistanceToGoal;
        AddReward(goalDelta * 0.01f);
        previousDistanceToGoal = currentDistanceToGoal;

        // 3-2. 적 회피 거리 보상 (적이 8m 이내로 접근 시)
        if (area != null && area.enemy != null && currentDistanceToEnemy < 8.0f)
        {
            float enemyDelta = currentDistanceToEnemy - previousDistanceToEnemy;
            AddReward(enemyDelta * 0.008f);

            // 초근접 위험 페널티 (2m 이내)
            if (currentDistanceToEnemy < 2.0f)
            {
                AddReward(-0.001f);
            }
            previousDistanceToEnemy = currentDistanceToEnemy;
        }

        // 3-3. 타임스텝 페널티 (빠른 클리어 유도)
        AddReward(-0.0005f);
    }

    private void UpdateDistances()
    {
        currentDistanceToGoal = 50.0f;
        currentDistanceToEnemy = 50.0f;

        if (area == null) return;

        Transform target = hasKey ? area.GetClosestDoor(transform.position) : area.GetClosestActiveKey(transform.position);
        if (target != null)
        {
            currentDistanceToGoal = Vector3.Distance(transform.position, target.position);
        }

        if (area.enemy != null)
        {
            currentDistanceToEnemy = Vector3.Distance(transform.position, area.enemy.transform.position);
        }
    }

    public void OnKeyCollected()
    {
        if (hasKey) return;
        hasKey = true;
        AddReward(1.5f);
        UpdateDistances();
        previousDistanceToGoal = currentDistanceToGoal;
    }

    public void OnEnemySoundHeard(Vector3 soundPos, string reason)
    {
        // VectorObservation의 area.EnemySoundPosition을 통해 자동 반영됨
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        Vector2 move = ReadKeyboardMovement(checkWasd: true, checkArrows: true, checkIjkl: true);
        var continuous = actionsOut.ContinuousActions;
        continuous[0] = move.x;
        continuous[1] = move.y;

        if (actionsOut.DiscreteActions.Length > 0)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            int boxAction = 0;
            if (kb != null)
            {
                if (kb.fKey.isPressed || kb.oKey.isPressed) boxAction = 2;
                else if (kb.eKey.isPressed || kb.uKey.isPressed) boxAction = 1;
            }
            var discrete = actionsOut.DiscreteActions;
            discrete[0] = boxAction;
        }
    }
}
