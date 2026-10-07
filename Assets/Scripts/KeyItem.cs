using UnityEngine;

public class KeyItem : MonoBehaviour
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
            if (area != null)
            {
                area.OnKeyCollected(this, transform.position);
            }
            gameObject.SetActive(false);
        }
    }
}
