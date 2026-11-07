using UnityEngine;
using Unity.MLAgents.Sensors;

/// <summary>
/// 미로 술래잡기 환경의 적(술래) 에이전트:
/// 플레이어를 추적하여 잡는 것을 목표로 학습합니다.
/// 소리를 감지하고 장애물(상자)을 해제하거나 이동하며 플레이어에게 접근합니다.
/// </summary>
public class EnemyAgent : MazeAgentBase
{
    /// <summary>벡터 관측 크기: 플레이어가 낸 소리(4) = 총 4차원</summary>
    public const int VectorObservationSize = 4;

    /// <summary>플레이어 체포(충돌) 시 지급되는 승리 보상</summary>
    private const float CatchReward = 1.0f;

    [Header("시야 감지 보상")]
    [Tooltip("에피소드 중 플레이어가 처음 시야 범위 내에 포착되었을 때 1회 부여할 보상")]
    public float playerInSightReward = 0.05f;

    [Header("수동 조작 (Heuristic)")]
    [Tooltip("WASD 키로 수동 조작 여부 (기본: true)")]
    public bool useWasd = true;

    [Tooltip("방향키(Arrow)로 수동 조작 여부 (기본: false, 플레이어와 분리)")]
    public bool useArrows = false;

    /// <summary>이번 에피소드에서 이미 플레이어를 시야로 포착하여 보상을 받았는지 여부</summary>
    private bool hasSpottedPlayer;

    /// <summary>WASD 조작 활성화 여부</summary>
    public override bool UseWasd => useWasd;

    /// <summary>방향키 조작 활성화 여부</summary>
    public override bool UseArrows => useArrows;

    /// <summary>에이전트가 발생시키는 소리의 종류 (Enemy)</summary>
    protected override SoundSource MySoundSource => SoundSource.Enemy;

    /// <summary>추적 대상인 플레이어의 현재 Transform 참조</summary>
    private Transform PlayerTarget => area != null ? area.GetPlayerTransform() : null;

    /// <summary>
    /// 에피소드 리셋 시 적의 위치, 물리 속도 및 최초 시야 포착 플래그를 초기화합니다.
    /// </summary>
    public override void ResetPosition()
    {
        base.ResetPosition();
        hasSpottedPlayer = false;
    }

    /// <summary>
    /// 적의 강화학습 벡터 관측(Vector Observation, 총 4차원)을 수집합니다.
    /// 1. 플레이어가 낸 소리의 상대 위치 및 남은 시간 비율 (4차원)
    /// </summary>
    /// <param name="sensor">관측 데이터를 기록할 벡터 센서</param>
    public override void CollectObservations(VectorSensor sensor)
    {
        // 1. 플레이어 소리 상대 위치(3차원) + 남은 시간 비율(1차원) (총 4차원)
        AddSoundObservation(sensor, SoundSource.Player);

        // 참고: 플레이어, 벽 및 상자 상태(Box / LockedBox) 탐지는 머리에 장착된 3D RayPerceptionSensor로 수행됩니다.
    }

    /// <summary>
    /// 보상 성형(Reward Shaping): 플레이어를 에피소드 중 '처음' 시야선(Raycast) 내에 포착했을 때 1회성 보상을 부여합니다.
    /// 지속 보상으로 인한 보상 해킹(자리 지키며 시야 유지하기)을 방지합니다.
    /// </summary>
    protected override void ApplyRewardShaping()
    {
        if (!hasSpottedPlayer)
        {
            Transform target = PlayerTarget;
            if (target != null && IsTargetInSight(target))
            {
                hasSpottedPlayer = true;
                AddReward(playerInSightReward);
            }
        }
    }

    /// <summary>
    /// 플레이어와의 물리적 충돌을 감지하여 체포 성공(에피소드 승리)을 처리합니다.
    /// </summary>
    /// <param name="collision">충돌 정보</param>
    private void OnCollisionEnter(Collision collision)
    {
        if (!collision.gameObject.CompareTag("Player")) return;

        if (area != null)
        {
            area.EndEpisodeByCatch();
        }
        else
        {
            AddReward(CatchReward);
            EndEpisode();
        }
    }
}
