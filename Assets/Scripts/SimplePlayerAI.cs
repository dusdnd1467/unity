using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

/// <summary>
/// 레거시 및 테스트용 단순 규칙 기반 플레이어 AI:
/// 강화학습(PlayerAgent) 이전 단계의 상태 기반 플레이어로, 더미 훈련이나 수동 테스트 시 사용됩니다.
/// - 더미 모드 (T 키로 토글): 제자리에 정지하여 훈련 샌드백 역할 (IJKL 키로 수동 이동 가능)
/// - 자율 모드: 열쇠(미획득 시) 또는 탈출문(획득 시)을 향해 NavMeshAgent로 최단 경로 자동 이동
/// </summary>
public class SimplePlayerAI : MonoBehaviour
{
    [Header("상태 및 이동 속도")]
    [Tooltip("열쇠 소지 여부")]
    public bool hasKey = false;

    [Tooltip("이동 속도")]
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

    /// <summary>
    /// 컴포넌트 참조를 연결하고 초기 더미 상태를 기록합니다.
    /// </summary>
    private void Awake()
    {
        EnsureReferences();
        lastDummyState = isTrainingDummy;
    }

    /// <summary>
    /// 소속 MazeArea 참조를 주입받아 초기화합니다.
    /// </summary>
    /// <param name="mazeArea">플레이어가 속한 MazeArea 인스턴스</param>
    public void Initialize(MazeArea mazeArea)
    {
        area = mazeArea;
        EnsureReferences();
    }

    /// <summary>
    /// NavMeshAgent 및 MazeArea 컴포넌트를 확인하고 초기 스폰 좌표를 캐싱합니다.
    /// </summary>
    private void EnsureReferences()
    {
        if (navAgent == null) navAgent = GetComponent<NavMeshAgent>();
        if (navAgent != null) navAgent.speed = moveSpeed;
        if (area == null) area = GetComponentInParent<MazeArea>();

        if (!initialPoseCaptured)
        {
            initialPosition = transform.position;
            initialRotation = transform.rotation;
            if (Application.isPlaying) initialPoseCaptured = true;
        }
    }

    /// <summary>
    /// 매 프레임 더미 모드 토글 입력(T), 수동 이동(IJKL) 또는 NavMesh 자동 목적지 갱신을 처리합니다.
    /// </summary>
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

    /// <summary>
    /// 더미 모드일 때 I, J, K, L 키 입력을 받아 플레이어를 수동으로 이동시킵니다.
    /// </summary>
    /// <param name="kb">키보드 인스턴스</param>
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

    /// <summary>
    /// 현재 열쇠 보유 상태에 따라 목적지(열쇠 또는 탈출문)를 결정하고 NavMeshAgent에 설정합니다.
    /// </summary>
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

    /// <summary>
    /// 더미 모드 활성화/비활성화 시 NavMeshAgent 이동 경로를 정지하거나 재개합니다.
    /// </summary>
    /// <param name="dummy">더미 모드 여부</param>
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

    /// <summary>
    /// 에피소드 리셋 시 플레이어의 위치, 회전, 열쇠 소지 상태 및 NavMeshAgent 워프를 수행합니다.
    /// </summary>
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

    /// <summary>
    /// 열쇠 획득 시 호출되어 목적지를 탈출문으로 재설정합니다.
    /// </summary>
    public void OnCollectedKey()
    {
        hasKey = true;
        UpdateDestination();
    }

    /// <summary>
    /// 적의 소리를 감지했을 때 호출되어 목적지 경로를 즉시 재탐색합니다.
    /// </summary>
    /// <param name="soundPos">소리 발생 위치</param>
    /// <param name="reason">소리 발생 사유</param>
    public void OnEnemySoundHeard(Vector3 soundPos, string reason)
    {
        if (!isTrainingDummy) UpdateDestination();
    }
}
