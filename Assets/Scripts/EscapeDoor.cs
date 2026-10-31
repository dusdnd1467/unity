using UnityEngine;

/// <summary>
/// 탈출문 컴포넌트: 열쇠를 소지한 플레이어(PlayerAgent 또는 SimplePlayerAI)가 도달하면 탈출 성공 처리합니다.
/// </summary>
public class EscapeDoor : MonoBehaviour
{
    private MazeArea area;

    public void Initialize(MazeArea mazeArea)
    {
        area = mazeArea;
    }

    private void Awake()
    {
        if (area == null) area = GetComponentInParent<MazeArea>();
    }

    private void OnTriggerEnter(Collider other) => TryEscape(other.gameObject);
    private void OnTriggerStay(Collider other) => TryEscape(other.gameObject);
    private void OnCollisionEnter(Collision collision) => TryEscape(collision.gameObject);

    private void TryEscape(GameObject target)
    {
        if (area == null) return;

        bool hasKey = (target.GetComponentInParent<PlayerAgent>()?.hasKey ?? false) ||
                      (target.GetComponentInParent<SimplePlayerAI>()?.hasKey ?? false);

        if (hasKey)
        {
            Debug.Log("[EscapeDoor] 플레이어가 열쇠를 획득하고 탈출문에 도착했습니다!");
            area.OnPlayerEscaped();
        }
    }
}
