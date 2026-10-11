using UnityEngine;
using UnityEngine.AI;

public class SimplePlayerAI : MonoBehaviour
{
    public bool hasKey = false;
    public float moveSpeed = 4.0f;

    [Header("더미 모드 설정 (인스펙터 또는 T 키로 토글 가능)")]
    [Tooltip("체크 시 플레이어가 제자리에 멈춰 서서 적의 훈련 타겟이 됩니다. 체크 해제 시 열쇠/탈출문을 찾아 이동합니다.")]
    public bool isTrainingDummy = true;

    private bool lastDummyState;
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
        lastDummyState = isTrainingDummy;
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
        lastDummyState = isTrainingDummy;
    }

    private void Update()
    {
        if (area == null)
        {
            area = GetComponentInParent<MazeArea>();
            if (area == null) area = Object.FindFirstObjectByType<MazeArea>();
        }

        if (navAgent == null) navAgent = GetComponent<NavMeshAgent>();

        // 1. 키보드 'T' 키로 더미 모드 실시간 켜기/끄기 토글
        if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.tKey.wasPressedThisFrame)
        {
            SetDummyMode(!isTrainingDummy);
        }

        // 2. 인스펙터 창에서 체크박스를 켜거나 껐을 때 즉시 감지하여 적용
        if (isTrainingDummy != lastDummyState)
        {
            SetDummyMode(isTrainingDummy);
        }

        if (navAgent == null || !navAgent.isOnNavMesh) return;
        if (isTrainingDummy) return;

        // 더미 모드가 아닐 때: 경로가 유실되었거나 없으면 목적지 재설정
        if (!navAgent.hasPath || navAgent.pathStatus == NavMeshPathStatus.PathInvalid)
        {
            UpdateDestination();
        }
    }

    /// <summary>
    /// 더미 모드를 켜거나 끄는 함수 (인스펙터 우클릭 메뉴에서도 실행 가능)
    /// </summary>
    [ContextMenu("Toggle Dummy Mode")]
    public void ToggleDummyMode()
    {
        SetDummyMode(!isTrainingDummy);
    }

    public void SetDummyMode(bool dummy)
    {
        isTrainingDummy = dummy;
        lastDummyState = dummy;

        if (navAgent == null) navAgent = GetComponent<NavMeshAgent>();

        if (isTrainingDummy)
        {
            if (navAgent != null && navAgent.isOnNavMesh)
            {
                navAgent.ResetPath();
            }
            Debug.Log("[SimplePlayerAI] 🛑 더미 모드 ON: 플레이어가 제자리에 멈춥니다.");
        }
        else
        {
            Debug.Log("[SimplePlayerAI] 🏃 더미 모드 OFF: 플레이어가 열쇠/탈출문을 향해 이동합니다.");
            UpdateDestination();
        }
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
            if (navAgent.isOnNavMesh)
            {
                navAgent.ResetPath();
                navAgent.Warp(initialPosition);
            }
            else
            {
                transform.position = initialPosition;
                transform.rotation = initialRotation;
            }
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
        if (area == null)
        {
            area = GetComponentInParent<MazeArea>();
            if (area == null) area = Object.FindFirstObjectByType<MazeArea>();
        }
        if (area == null) return;

        if (navAgent == null) navAgent = GetComponent<NavMeshAgent>();
        if (navAgent == null || !navAgent.isOnNavMesh) return;

        // 더미 모드일 경우 이동하지 않고 대기
        if (isTrainingDummy)
        {
            navAgent.ResetPath();
            return;
        }

        if (!hasKey)
        {
            // 아직 열쇠를 얻지 못한 경우: 가장 가까운 열쇠로 이동
            Transform targetKey = area.GetClosestActiveKey(transform.position);
            if (targetKey != null)
            {
                navAgent.SetDestination(targetKey.position);
            }
        }
        else
        {
            // 열쇠를 얻은 경우: 가장 가까운 탈출 문으로 이동
            Transform targetDoor = area.GetClosestDoor(transform.position);
            if (targetDoor != null)
            {
                Vector3 destPos = targetDoor.position;
                // 문 위치가 NavMesh 가장자리에 있을 경우 가장 가까운 유효 NavMesh 지점으로 보정
                if (NavMesh.SamplePosition(targetDoor.position, out NavMeshHit hit, 5.0f, NavMesh.AllAreas))
                {
                    destPos = hit.position;
                }
                navAgent.SetDestination(destPos);
            }
        }
    }
}
