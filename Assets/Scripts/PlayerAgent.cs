using UnityEngine;
using Unity.MLAgents.Sensors;

/// <summary>
/// 미로 술래잡기 환경의 플레이어 에이전트 (강화학습):
/// 1. 열쇠(KeyItem)를 탐색해 획득 (+0.5 보상)
/// 2. 탈출문(EscapeDoor)을 찾아 탈출 (+1.0 보상)
/// 3. 적(EnemyAgent)의 추적을 회피 (체포 시 -1.0 보상)
/// 4. 상자(MovableBox)를 조작하여 적의 진로를 방해하거나 길을 확보
/// </summary>
public class PlayerAgent : MazeAgentBase
{
    /// <summary>벡터 관측 크기: 열쇠(1) + 적 소리(4) = 총 5차원</summary>
    public const int VectorObservationSize = 5;

    private const float KeyReward = 0.5f;
    private const float GoalProgressRewardScale = 0.003f;
    private const float EnemyAvoidRange = 8.0f;
    private const float EnemyAvoidRewardScale = 0.002f;
    private const float EnemyDangerRange = 2.0f;
    private const float EnemyDangerPenalty = -0.001f;
    private const float FarDistance = 50.0f;

    [Header("수동 조작 (Heuristic)")]
    [Tooltip("방향키(Arrow)로 수동 조작 여부 (기본: true)")]
    public bool useArrows = true;

    [Tooltip("WASD 키로 수동 조작 여부 (기본: false, 필요 시 켜서 단독 플레이 가능)")]
    public bool useWasd = false;

    [Header("상태")]
    public bool hasKey = false;

    [Header("디버그 정보 (읽기 전용)")]
    [SerializeField] private float currentDistanceToGoal;
    [SerializeField] private float currentDistanceToEnemy;

    private float previousDistanceToGoal;
    private float previousDistanceToEnemy;

    public override bool UseWasd => useWasd;
    public override bool UseArrows => useArrows;
    protected override SoundSource MySoundSource => SoundSource.Player;

    private Transform CurrentGoal => area == null ? null : (hasKey ? area.GetClosestDoor(transform.position) : area.GetClosestActiveKey(transform.position));

    /// <summary>하위 호환용 래퍼</summary>
    public void InitializePlayer(MazeArea mazeArea) => InitializeAgent(mazeArea);

    public override void ResetPosition()
    {
        base.ResetPosition();
        hasKey = false;
    }

    public override void RefreshTracking()
    {
        UpdateDistances();
        previousDistanceToGoal = currentDistanceToGoal;
        previousDistanceToEnemy = currentDistanceToEnemy;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // 1. 열쇠 소지 여부 (1차원)
        sensor.AddObservation(hasKey ? 1.0f : 0.0f);

        // 2. 적 소리 상대 위치(3차원) + 남은 시간 비율(1차원) (총 4차원)
        AddSoundObservation(sensor, SoundSource.Enemy);

        // 참고: 목표(열쇠/문), 상자, 상대방의 물리적 탐지는 머리에 장착된 3D RayPerceptionSensor로 수행됩니다.
    }

    protected override void ApplyRewardShaping()
    {
        UpdateDistances();

        // 1. 목표(열쇠/탈출문) 접근 보상
        AddReward((previousDistanceToGoal - currentDistanceToGoal) * GoalProgressRewardScale);
        previousDistanceToGoal = currentDistanceToGoal;

        // 2. 적 회피 거리 보상 (근접 시에만 적용)
        if (currentDistanceToEnemy < EnemyAvoidRange)
        {
            AddReward((currentDistanceToEnemy - previousDistanceToEnemy) * EnemyAvoidRewardScale);

            if (currentDistanceToEnemy < EnemyDangerRange)
            {
                AddReward(EnemyDangerPenalty);
            }
        }
        previousDistanceToEnemy = currentDistanceToEnemy;
    }

    private void UpdateDistances()
    {
        Transform goal = CurrentGoal;
        currentDistanceToGoal = goal != null ? Vector3.Distance(transform.position, goal.position) : FarDistance;

        Transform enemy = area != null && area.enemy != null ? area.enemy.transform : null;
        currentDistanceToEnemy = enemy != null ? Vector3.Distance(transform.position, enemy.position) : FarDistance;
    }

    public void OnKeyCollected()
    {
        if (hasKey) return;
        hasKey = true;
        AddReward(KeyReward);

        // 목표가 열쇠 → 탈출문으로 전환되므로 거리 기준값 재설정
        UpdateDistances();
        previousDistanceToGoal = currentDistanceToGoal;
    }
}
