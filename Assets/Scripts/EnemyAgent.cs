using UnityEngine;
using Unity.MLAgents.Sensors;

/// <summary>
/// 미로 술래잡기 환경의 적 에이전트:
/// 플레이어를 추적하여 잡는 것을 목표로 학습합니다.
/// 소리를 감지하고 장애물(상자)을 해제하며 플레이어에게 접근합니다.
/// </summary>
public class EnemyAgent : MazeAgentBase
{
    /// <summary>벡터 관측 크기: 플레이어가 낸 소리 (상대 위치 3 + 남은 시간 비율 1)</summary>
    public const int VectorObservationSize = 4;

    private const float CatchReward = 1.0f;
    private const float DistanceRewardScale = 0.01f;

    [Header("수동 조작 (Heuristic)")]
    [Tooltip("WASD 키로 수동 조작 여부 (기본: true)")]
    public bool useWasd = true;

    [Tooltip("방향키(Arrow)로 수동 조작 여부 (기본: false, 플레이어와 분리)")]
    public bool useArrows = false;

    [Header("상자 상호작용")]
    [Tooltip("플레이어가 고정해 둔 상자 근처에 접근 시 자동으로 해제를 시도할지 여부 (RL 제어 시에만 적용)")]
    public bool autoUnlockPlayerLockedBox = true;

    private float previousDistanceToPlayer;

    public override bool UseWasd => useWasd;
    public override bool UseArrows => useArrows;
    protected override SoundSource MySoundSource => SoundSource.Enemy;
    protected override bool AutoUnlockOpponentBox => autoUnlockPlayerLockedBox;

    private Transform PlayerTarget => area != null ? area.GetPlayerTransform() : null;

    /// <summary>하위 호환용 래퍼</summary>
    public void InitializeEnemy(MazeArea mazeArea) => InitializeAgent(mazeArea);

    public override void RefreshTracking()
    {
        Transform target = PlayerTarget;
        if (target != null)
        {
            previousDistanceToPlayer = Vector3.Distance(transform.position, target.position);
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        AddSoundObservation(sensor, SoundSource.Player);
    }

    protected override void ApplyRewardShaping()
    {
        Transform target = PlayerTarget;
        if (target == null) return;

        float currentDist = Vector3.Distance(transform.position, target.position);
        AddReward((previousDistanceToPlayer - currentDist) * DistanceRewardScale);
        previousDistanceToPlayer = currentDist;
    }

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
