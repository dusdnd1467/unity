using UnityEngine;

public class EscapeDoor : MonoBehaviour
{
    private MazeArea area;

    public void Initialize(MazeArea mazeArea)
    {
        area = mazeArea;
    }

    private void Awake()
    {
        EnsureAreaReference();
    }

    private void EnsureAreaReference()
    {
        if (area == null)
        {
            area = GetComponentInParent<MazeArea>();
            if (area == null)
            {
                area = Object.FindFirstObjectByType<MazeArea>();
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        TryEscape(other.gameObject);
    }

    private void OnTriggerStay(Collider other)
    {
        TryEscape(other.gameObject);
    }

    private void OnCollisionEnter(Collision collision)
    {
        TryEscape(collision.gameObject);
    }

    private void TryEscape(GameObject target)
    {
        var player = target.GetComponentInParent<SimplePlayerAI>();
        if (player != null && player.hasKey)
        {
            EnsureAreaReference();
            if (area != null)
            {
                Debug.Log("[EscapeDoor] 플레이어가 열쇠를 획득하고 문에 도착했습니다! 에피소드를 리셋합니다.");
                area.OnPlayerEscaped();
            }
        }
    }
}
