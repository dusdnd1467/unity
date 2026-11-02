using UnityEngine;

/// <summary>
/// 열쇠 아이템: 플레이어가 닿으면 획득 처리 후 비활성화됩니다.
/// 이미 열쇠를 가진 플레이어는 추가로 획득하지 않습니다.
/// </summary>
public class KeyItem : MonoBehaviour
{
    private MazeArea area;

    public void Initialize(MazeArea mazeArea)
    {
        area = mazeArea;
    }

    public void ResetKey(Vector3 newPosition)
    {
        transform.position = newPosition;
        gameObject.SetActive(true);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        if (area == null)
        {
            gameObject.SetActive(false);
            return;
        }

        if (area.TryCollectKey(transform.position))
        {
            gameObject.SetActive(false);
        }
    }
}
