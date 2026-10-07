using UnityEngine;

public class EscapeDoor : MonoBehaviour
{
    private MazeArea area;

    public void Initialize(MazeArea mazeArea)
    {
        area = mazeArea;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            var player = other.GetComponent<SimplePlayerAI>();
            if (player != null && player.hasKey)
            {
                if (area != null)
                {
                    area.OnPlayerEscaped();
                }
            }
        }
    }
}
