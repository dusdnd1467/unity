using UnityEngine;

/// <summary>
/// 열쇠 아이템:
/// 플레이어(PlayerAgent)가 접촉 시 획득 처리되며, 오브젝트가 비활성화됩니다.
/// 이미 열쇠를 보유한 상태라면 중복 획득되지 않습니다.
/// </summary>
public class KeyItem : MonoBehaviour
{
    private MazeArea area;
    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private bool initialPoseCaptured;

    /// <summary>
    /// 소속 MazeArea 참조를 주입받고 초기 스폰 위치/회전 상태를 기록합니다.
    /// </summary>
    /// <param name="mazeArea">열쇠가 속한 MazeArea 인스턴스</param>
    public void Initialize(MazeArea mazeArea)
    {
        area = mazeArea;
        EnsureInitialPose();
    }

    /// <summary>
    /// 씬 로드 시 초기 배치 좌표를 기록합니다.
    /// </summary>
    private void Awake()
    {
        EnsureInitialPose();
    }

    /// <summary>
    /// 에피소드 리셋 시 되돌아갈 시작 위치 및 회전값을 캐싱합니다. (런타임에 최초 1회 확정)
    /// </summary>
    private void EnsureInitialPose()
    {
        if (!initialPoseCaptured)
        {
            initialPosition = transform.position;
            initialRotation = transform.rotation;
            if (Application.isPlaying) initialPoseCaptured = true;
        }
    }

    /// <summary>
    /// 새로운 에피소드 시작 시 열쇠의 위치를 초기 스폰 지점으로 복구하고 다시 활성화합니다.
    /// </summary>
    public void ResetKey()
    {
        EnsureInitialPose();
        transform.SetPositionAndRotation(initialPosition, initialRotation);
        gameObject.SetActive(true);
    }

    /// <summary>
    /// 플레이어와의 접촉을 감지하여 열쇠 획득 절차를 실행합니다.
    /// </summary>
    /// <param name="other">접촉한 대상 콜라이더</param>
    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        if (area == null)
        {
            gameObject.SetActive(false);
            return;
        }

        // 플레이어가 아직 열쇠가 없는 경우에만 획득 성공 및 비활성화
        if (area.TryCollectKey(transform.position))
        {
            gameObject.SetActive(false);
        }
    }
}
