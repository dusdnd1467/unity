using UnityEngine;

/// <summary>
/// 열쇠 아이템:
/// 플레이어(PlayerAgent)가 접촉 시 획득 처리되며, 오브젝트가 비활성화됩니다.
/// 이미 열쇠를 보유한 상태라면 중복 획득되지 않습니다.
/// </summary>
public class KeyItem : MonoBehaviour
{
    private MazeArea area;
    private InitialPose initialPose;

    /// <summary>
    /// 소속 MazeArea 참조를 주입받고 초기 스폰 위치/회전 상태를 기록합니다.
    /// </summary>
    public void Initialize(MazeArea mazeArea)
    {
        area = mazeArea;
        initialPose.CaptureOnce(transform);
    }

    private void Awake()
    {
        initialPose.CaptureOnce(transform);
    }

    /// <summary>
    /// 새로운 에피소드 시작 시 열쇠의 위치를 초기 스폰 지점으로 복구하고 다시 활성화합니다.
    /// </summary>
    public void ResetKey()
    {
        initialPose.CaptureOnce(transform);
        initialPose.Restore(transform);
        gameObject.SetActive(true);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        // 플레이어가 아직 열쇠가 없는 경우에만 획득 성공 및 비활성화
        if (area.TryCollectKey(transform.position))
        {
            gameObject.SetActive(false);
        }
    }
}
