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
    /// <summary>열쇠 획득 시 1회 부여되는 즉발 보상</summary>
    private const float KeyReward = 0.5f;

    [Header("상태")]
    [Tooltip("현재 열쇠를 소지하고 있는지 여부")]
    public bool hasKey = false;

    /// <summary>에이전트가 발생시키는 소리의 종류 (Player)</summary>
    protected override SoundSource MySoundSource => SoundSource.Player;

    private void Reset()
    {
        useWasd = false;
        useArrows = true;
    }

    /// <summary>
    /// 에피소드 리셋 시 플레이어의 위치, 물리 속도 및 열쇠 소지 상태를 초기화합니다.
    /// </summary>
    public override void ResetPosition()
    {
        base.ResetPosition();
        hasKey = false;
    }

    /// <summary>
    /// 플레이어의 강화학습 벡터 관측(Vector Observation, 총 6차원)을 수집합니다.
    /// 1. 열쇠 소지 여부 (1차원)
    /// 2. 적이 낸 소리의 상대 위치 및 남은 시간 비율 (4차원)
    /// 3. 상자 운반 여부 (1차원)
    /// </summary>
    public override void CollectObservations(VectorSensor sensor)
    {
        sensor.AddObservation(hasKey ? 1.0f : 0.0f);
        AddSoundObservation(sensor, SoundSource.Enemy);
        AddBoxCarryObservation(sensor);
    }

    /// <summary>
    /// 열쇠를 습득했을 때 호출되어 소지 상태를 갱신하고 보상을 지급합니다.
    /// </summary>
    public void OnKeyCollected()
    {
        if (hasKey) return;
        hasKey = true;
        AddReward(KeyReward);
    }
}
