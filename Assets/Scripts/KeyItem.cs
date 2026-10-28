using UnityEngine;

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
        if (other.CompareTag("Player"))
        {
            if (area != null)
            {
                area.OnKeyCollected(this, transform.position);
            }
            gameObject.SetActive(false);
        }
    }
}
