using UnityEngine;

/// <summary>
/// 탈출문 컴포넌트:
/// 열쇠를 소지한 플레이어(PlayerAgent)가 문 영역(Trigger)에 도달하면 탈출 성공(에피소드 승리)을 처리합니다.
/// </summary>
public class EscapeDoor : MonoBehaviour
{
    private MazeArea area;

    /// <summary>
    /// 소속된 MazeArea 매니저 참조를 주입받아 초기화합니다.
    /// </summary>
    /// <param name="mazeArea">문이 속한 MazeArea 인스턴스</param>
    public void Initialize(MazeArea mazeArea)
    {
        area = mazeArea;
    }

    /// <summary>
    /// 씬 로드 시 부모 오브젝트에서 MazeArea를 자동 탐색하여 캐싱합니다.
    /// </summary>
    private void Awake()
    {
        if (area == null) area = GetComponentInParent<MazeArea>();
    }

    /// <summary>
    /// 플레이어가 트리거 콜라이더 영역에 진입했을 때 탈출 가능 여부를 검사합니다.
    /// </summary>
    /// <param name="other">충돌한 상대 콜라이더</param>
    private void OnTriggerEnter(Collider other) => TryEscape(other.gameObject);

    /// <summary>
    /// 플레이어가 이미 문 트리거 내에 있는 상태에서 열쇠를 습득한 경우를 위한 안전 폴백(Fallback) 검사입니다.
    /// </summary>
    /// <param name="other">머무르고 있는 상대 콜라이더</param>
    private void OnTriggerStay(Collider other) => TryEscape(other.gameObject);

    /// <summary>
    /// 대상 오브젝트가 열쇠를 소지한 유효한 플레이어인지 검증 후, MazeArea에 탈출 성공을 통보합니다.
    /// </summary>
    /// <param name="target">문과 접촉한 게임 오브젝트</param>
    private void TryEscape(GameObject target)
    {
        if (!area.ActorHasKey(target)) return;

        Debug.Log("[EscapeDoor] 플레이어가 열쇠를 획득하고 탈출문에 도착했습니다!");
        area.OnPlayerEscaped();
    }
}
