using UnityEngine;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

/// <summary>
/// 미로 술래잡기 환경의 적 에이전트:
/// 플레이어를 추적하여 잡는 것을 목표로 학습합니다.
/// 소리를 감지하고 장애물(상자)을 해제하며 플레이어에게 접근합니다.
/// </summary>
public class EnemyAgent : MazeAgentBase
{
    [Header("상자 상호작용")]
    [Tooltip("플레이어가 고정해 둔 상자 근처에 접근 시 자동으로 해제를 시도할지 여부")]
    public bool autoUnlockPlayerLockedBox = true;

    private float previousDistanceToPlayer;

    public void InitializeEnemy(MazeArea mazeArea)
    {
        InitializeAgent(mazeArea);
    }

    public override void OnEpisodeBegin()
    {
        ReleaseActiveBox();
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

        Transform playerTarget = GetPlayerTarget();
        if (playerTarget != null)
        {
            previousDistanceToPlayer = Vector3.Distance(transform.position, playerTarget.position);
        }
    }

    private void Update()
    {
        // Heuristic 수동 테스트 시 키보드 입력 감지 (F: 고정, E: 잡기/해제)
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return;

        if (kb.fKey.wasPressedThisFrame)
        {
            HandleBoxInteraction(2, SoundSource.Enemy, Time.deltaTime, false);
        }

        isHoldingInteractKey = kb.eKey.isPressed;
        if (isHoldingInteractKey)
        {
            HandleBoxInteraction(1, SoundSource.Enemy, Time.deltaTime, false, () => AddReward(0.05f));
        }
        else if (kb.eKey.wasReleasedThisFrame)
        {
            ReleaseActiveBox();
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // 플레이어가 발생시킨 소리 정보 관측 (상대 위치 3차원 + 남은 시간 비율 1차원 = 4차원)
        if (area != null && area.PlayerSoundTimer > 0f)
        {
            Vector3 soundRel = area.PlayerSoundPosition - transform.position;
            sensor.AddObservation(soundRel * 0.05f);
            sensor.AddObservation(area.PlayerSoundTimer / Mathf.Max(0.01f, area.soundDuration));
        }
        else
        {
            sensor.AddObservation(Vector3.zero);
            sensor.AddObservation(0f);
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // 1. 상자 상호작용
        if (!isHoldingInteractKey)
        {
            int boxAction = actions.DiscreteActions.Length > 0 ? actions.DiscreteActions[0] : 0;
            HandleBoxInteraction(boxAction, SoundSource.Enemy, Time.fixedDeltaTime, autoUnlockPlayerLockedBox,
                onUnlockSuccess: () => AddReward(0.05f));
        }

        // 2. 물리 이동 (상자 해제 채널링 중에는 제자리 정지)
        float currentSpeed = IsChannelingBox() ? 0f : moveSpeed;
        Vector3 moveInput = new Vector3(actions.ContinuousActions[0], 0f, actions.ContinuousActions[1]);
        MoveAgent(moveInput, currentSpeed, Time.fixedDeltaTime);

        // 3. 거리 기반 보상 성형(Reward Shaping) 및 타임스텝 페널티
        Transform playerTarget = GetPlayerTarget();
        if (playerTarget != null)
        {
            float currentDist = Vector3.Distance(transform.position, playerTarget.position);
            AddReward((previousDistanceToPlayer - currentDist) * 0.01f);
            previousDistanceToPlayer = currentDist;
        }

        AddReward(-0.0005f);
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        Vector2 move = ReadKeyboardMovement(checkWasd: true, checkArrows: true);
        var continuous = actionsOut.ContinuousActions;
        continuous[0] = move.x;
        continuous[1] = move.y;

        if (actionsOut.DiscreteActions.Length > 0)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            int boxAction = 0;
            if (kb != null)
            {
                if (kb.fKey.isPressed) boxAction = 2;
                else if (kb.eKey.isPressed) boxAction = 1;
            }
            var discrete = actionsOut.DiscreteActions;
            discrete[0] = boxAction;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Player"))
        {
            AddReward(1.0f);
            if (area != null) area.EndEpisodeByCatch();
            else EndEpisode();
        }
    }

    private Transform GetPlayerTarget()
    {
        if (area == null) return null;
        if (area.player != null) return area.player.transform;
        if (area.legacyPlayerAI != null) return area.legacyPlayerAI.transform;
        return null;
    }
}
