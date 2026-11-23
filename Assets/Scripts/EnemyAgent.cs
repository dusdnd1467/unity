using UnityEngine;
using Unity.MLAgents.Sensors;

/// <summary>
/// 미로 술래잡기 환경의 적(술래) 에이전트:
/// 플레이어를 추적하여 잡는 것을 목표로 학습합니다. (체포 +1.0 / 타임아웃 0 / 탈출 허용 -1.0)
/// 소리를 감지하고 장애물(상자)을 해제하거나 이동하며 플레이어에게 접근합니다.
/// </summary>
public class EnemyAgent : MazeAgentBase
{
    /// <summary>에이전트가 발생시키는 소리의 종류 (Enemy)</summary>
    protected override SoundSource MySoundSource => SoundSource.Enemy;

    private void Reset()
    {
        useWasd = true;
        useArrows = false;
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

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Player"))
        {
            area.EndEpisodeByCatch();
        }
    }
}
