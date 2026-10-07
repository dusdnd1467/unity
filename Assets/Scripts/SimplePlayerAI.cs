using UnityEngine;
using UnityEngine.AI;

public class SimplePlayerAI : MonoBehaviour
{
    public bool hasKey = false;
    public float moveSpeed = 4.0f;

    private NavMeshAgent navAgent;
    private MazeArea area;
    private Vector3 initialPosition;
    private Quaternion initialRotation;

    private void Awake()
    {
        initialPosition = transform.position;
        initialRotation = transform.rotation;
        navAgent = GetComponent<NavMeshAgent>();
        if (navAgent != null)
        {
            navAgent.speed = moveSpeed;
        }
    }

    public void Initialize(MazeArea mazeArea)
    {
        area = mazeArea;
        if (navAgent == null)
        {
            navAgent = GetComponent<NavMeshAgent>();
        }
        if (navAgent != null)
        {
            navAgent.speed = moveSpeed;
        }
        initialPosition = transform.position;
        initialRotation = transform.rotation;
    }

    public void ResetPlayer()
    {
        hasKey = false;
        if (navAgent == null)
        {
            navAgent = GetComponent<NavMeshAgent>();
        }

        if (navAgent != null)
        {
            navAgent.Warp(initialPosition);
            navAgent.ResetPath();
        }
        else
        {
            transform.position = initialPosition;
            transform.rotation = initialRotation;
        }

        UpdateDestination();
    }

    public void OnCollectedKey()
    {
        hasKey = true;
        UpdateDestination();
    }

    public void UpdateDestination()
    {
        if (area == null) return;
        if (navAgent == null) navAgent = GetComponent<NavMeshAgent>();
        if (navAgent == null) return;

        if (!hasKey)
        {
            // Find closest active key
            Transform targetKey = area.GetClosestActiveKey(transform.position);
            if (targetKey != null)
            {
                navAgent.SetDestination(targetKey.position);
            }
        }
        else
        {
            // Find closest door
            Transform targetDoor = area.GetClosestDoor(transform.position);
            if (targetDoor != null)
            {
                navAgent.SetDestination(targetDoor.position);
            }
        }
    }
}
