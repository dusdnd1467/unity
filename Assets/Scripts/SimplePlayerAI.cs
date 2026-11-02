using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

/// <summary>
/// 레거시/테스트용 단순 플레이어 AI:
/// 강화학습(PlayerAgent) 이전의 규칙 기반 플레이어로, 더미 훈련이나 수동 테스트 시 사용됩니다.
/// - 더미 모드(T 키): 제자리에 정지 (IJKL 키로 수동 이동 가능)
/// - 자율 모드: 열쇠(미획득 시) 또는 탈출문(획득 시)을 향해 NavMesh로 이동
/// </summary>
public class SimplePlayerAI : MonoBehaviour
{
    public bool hasKey = false;
    public float moveSpeed = 4.0f;

    [Header("더미 모드 설정 (T 키로 토글)")]
    [Tooltip("체크 시 제자리에 정지하여 훈련 더미가 됩니다.")]
    public bool isTrainingDummy = true;

    private NavMeshAgent navAgent;
    private MazeArea area;
    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private bool lastDummyState;
    private bool initialPoseCaptured;

    private void Awake()
    {
        EnsureReferences();
        lastDummyState = isTrainingDummy;
    }

    public void Initialize(MazeArea mazeArea)
    {
        area = mazeArea;
        EnsureReferences();
    }

    private void EnsureReferences()
    {
        if (navAgent == null) navAgent = GetComponent<NavMeshAgent>();
        if (navAgent != null) navAgent.speed = moveSpeed;
        if (area == null) area = GetComponentInParent<MazeArea>();

        if (!initialPoseCaptured)
        {
            initialPosition = transform.position;
            initialRotation = transform.rotation;
            initialPoseCaptured = true;
        }
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.tKey.wasPressedThisFrame)
        {
            isTrainingDummy = !isTrainingDummy;
        }

        if (isTrainingDummy != lastDummyState)
        {
            SetDummyMode(isTrainingDummy);
        }

        if (isTrainingDummy)
        {
            HandleManualMove(kb);
            return;
        }

        if (navAgent != null && navAgent.isOnNavMesh && (!navAgent.hasPath || navAgent.pathStatus == NavMeshPathStatus.PathInvalid))
        {
            UpdateDestination();
        }
    }

    private void HandleManualMove(Keyboard kb)
    {
        if (kb == null) return;

        float mx = (kb.lKey.isPressed ? 1f : 0f) - (kb.jKey.isPressed ? 1f : 0f);
        float mz = (kb.iKey.isPressed ? 1f : 0f) - (kb.kKey.isPressed ? 1f : 0f);

        Vector3 move = new Vector3(mx, 0f, mz);
        if (move.sqrMagnitude > 0.01f)
        {
            Vector3 delta = move.normalized * moveSpeed * Time.deltaTime;
            if (navAgent != null && navAgent.isOnNavMesh)
            {
                navAgent.Move(delta);
            }
            else
            {
                transform.position += delta;
            }
        }
    }

    public void UpdateDestination()
    {
        if (area == null || navAgent == null || !navAgent.isOnNavMesh || isTrainingDummy) return;

        Transform target = hasKey ? area.GetClosestDoor(transform.position) : area.GetClosestActiveKey(transform.position);
        if (target != null)
        {
            Vector3 dest = target.position;
            if (NavMesh.SamplePosition(dest, out NavMeshHit hit, 3.0f, NavMesh.AllAreas))
            {
                dest = hit.position;
            }
            navAgent.isStopped = false;
            navAgent.SetDestination(dest);
        }
    }

    public void SetDummyMode(bool dummy)
    {
        isTrainingDummy = dummy;
        lastDummyState = dummy;

        if (navAgent != null && navAgent.isOnNavMesh)
        {
            if (dummy)
            {
                navAgent.ResetPath();
                navAgent.isStopped = true;
            }
            else
            {
                navAgent.isStopped = false;
                UpdateDestination();
            }
        }
    }

    public void ResetPlayer()
    {
        hasKey = false;
        transform.SetPositionAndRotation(initialPosition, initialRotation);

        if (navAgent != null && navAgent.isOnNavMesh)
        {
            navAgent.isStopped = false;
            navAgent.ResetPath();
            navAgent.Warp(initialPosition);
        }

        UpdateDestination();
    }

    public void OnCollectedKey()
    {
        hasKey = true;
        UpdateDestination();
    }

    public void OnEnemySoundHeard(Vector3 soundPos, string reason)
    {
        if (!isTrainingDummy) UpdateDestination();
    }
}
