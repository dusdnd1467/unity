using UnityEngine;
using Unity.MLAgents.Sensors;

/// <summary>
/// 미로 술래잡기 환경의 적(술래) 에이전트:
/// 플레이어를 추적하여 잡는 것을 목표로 학습합니다.
/// 소리를 감지하고 장애물(상자)을 해제하거나 이동하며 플레이어에게 접근합니다.
/// </summary>
public class EnemyAgent : MazeAgentBase
{
    [Header("시야 감지 보상")]
    [Tooltip("에피소드 중 플레이어가 처음 시야 범위 내에 포착되었을 때 1회 부여할 보상")]
    public float playerInSightReward = 0.05f;

    /// <summary>이번 에피소드에서 이미 플레이어를 시야로 포착하여 보상을 받았는지 여부</summary>
    private bool hasSpottedPlayer;

    /// <summary>에이전트가 발생시키는 소리의 종류 (Enemy)</summary>
    protected override SoundSource MySoundSource => SoundSource.Enemy;

    private void Reset()
    {
        useWasd = true;
        useArrows = false;
    }

    /// <summary>
    /// 에피소드 리셋 시 적의 위치, 물리 속도 및 최초 시야 포착 플래그를 초기화합니다.
    /// </summary>
    public override void ResetPosition()
    {
        base.ResetPosition();
        hasSpottedPlayer = false;
    }

    /// <summary>
    /// 적의 강화학습 벡터 관측(Vector Observation, 총 5차원)을 수집합니다.
    /// 1. 플레이어가 낸 소리의 상대 위치 및 남은 시간 비율 (4차원)
    /// 2. 상자 운반 여부 (1차원)
    /// </summary>
    public override void CollectObservations(VectorSensor sensor)
    {
        AddSoundObservation(sensor, SoundSource.Player);
        AddBoxCarryObservation(sensor);
    }

    /// <summary>
    /// 보상 성형(Reward Shaping): 플레이어를 에피소드 중 '처음' 시야선(Raycast) 내에 포착했을 때 1회성 보상을 부여합니다.
    /// 지속 보상으로 인한 보상 해킹(자리 지키며 시야 유지하기)을 방지합니다.
    /// </summary>
    protected override void ApplyRewardShaping()
    {
        if (hasSpottedPlayer) return;

        Transform target = area.GetPlayerTransform();
        if (target != null && IsTargetInSight(target))
        {
            hasSpottedPlayer = true;
            AddReward(playerInSightReward);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Player"))
        {
            area.EndEpisodeByCatch();
        }
    }
}
