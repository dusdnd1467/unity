using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;

/// <summary>
/// 미로 술래잡기 환경의 공통 에이전트 기반 클래스 (PlayerAgent 및 EnemyAgent의 부모 클래스).
/// 물리 속도 기반 이동, 상자(MovableBox) 조작 상호작용, 수동 조작(Heuristic),
/// 소리 및 주변 환경 관측 수집, 시작 자세 기록 및 리셋 파이프라인을 캡슐화합니다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public abstract class MazeAgentBase : Agent
{
    /// <summary>위치 관측값 정규화 계수 (월드 좌표 거리를 신경망 입력 범위인 약 [-1, 1]로 스케일링)</summary>
    protected const float ObservationPositionScale = 0.05f;

    /// <summary>타임스텝 페널티 (에이전트가 목표 달성을 지연하지 않고 빠르게 행동하도록 유도)</summary>
    protected const float DefaultTimeStepPenalty = -0.00005f;

    /// <summary>상자 상호작용 액션: 현재 상태 유지</summary>
    private const int BoxActionNone = 0;

    /// <summary>상자 상호작용 액션: 토글 (잡기 / 내려놓기 후 자동 잠금 / 잠금 해제 시작)</summary>
    private const int BoxActionToggleGrab = 1;

    /// <summary>WASD 조작 체계의 상자 상호작용 키 (E, C)</summary>
    private static readonly Key[] WasdInteractKeys = { Key.E, Key.C };

    /// <summary>방향키 조작 체계의 상자 상호작용 키 (Space, RightCtrl, Numpad0, Enter, U)</summary>
    private static readonly Key[] ArrowInteractKeys = { Key.Space, Key.RightCtrl, Key.Numpad0, Key.Enter, Key.U };

    [Header("이동 설정")]
    [Tooltip("에이전트 이동 속도")]
    public float moveSpeed = 5.0f;

    [Header("수동 조작 (Heuristic)")]
    [Tooltip("WASD 키로 수동 조작 여부")]
    public bool useWasd;

    [Tooltip("방향키(Arrow)로 수동 조작 여부")]
    public bool useArrows;

    protected Rigidbody rb;
    protected MazeArea area;

    private BehaviorParameters behaviorParameters;
    private Collider[] cachedColliders;
    private InitialPose initialPose;

    /// <summary>현재 에이전트가 잡고 있거나 잠금 해제 채널링 중인 상자</summary>
    private MovableBox activeBox;

    /// <summary>수동 조작(Heuristic) 모드에서 Update 프레임 동안 눌린 키 입력을 보관하는 래치 변수</summary>
    private int latchedTapAction;

    /// <summary>강화학습 정책의 이전 상자 액션 (0->1 변경 순간인 Rising Edge 감지용)</summary>
    private int lastPolicyBoxAction;

    public bool UseWasd => useWasd;
    public bool UseArrows => useArrows;

    /// <summary>상자 조작 시 발생하는 소리의 주체</summary>
    public SoundSource SoundSource => MySoundSource;

    /// <summary>캐싱된 에이전트 콜라이더 배열 (물리 충돌 무시 전환 시 GC 할당 방지)</summary>
    public Collider[] Colliders => cachedColliders;

    /// <summary>현재 에이전트가 상자를 잡고 운반 중인지 여부</summary>
    public bool IsCarryingBox => activeBox != null && activeBox.CurrentCarrier == gameObject;

    /// <summary>파생 클래스에서 정의하는 고유 소리 주체 (Player / Enemy)</summary>
    protected abstract SoundSource MySoundSource { get; }

    /// <summary>수동 조작(Heuristic) 모드 여부 확인</summary>
    protected bool IsHeuristicMode => behaviorParameters != null && behaviorParameters.IsInHeuristicMode();

    /// <summary>현재 에이전트가 키보드로 수동 제어 중인지 여부</summary>
    private bool IsManualControlActive => (UseWasd || UseArrows) && IsHeuristicMode;

    // ───────────────────────────── 라이프사이클 / 콜백 ─────────────────────────────

    /// <summary>
    /// 에이전트별 특화 보상 성형(Reward Shaping)을 처리하기 위한 가상 메서드입니다.
    /// OnActionReceived 단계에서 매 스텝마다 호출됩니다.
    /// </summary>
    protected virtual void ApplyRewardShaping() { }

    // ───────────────────────────── 초기화 / 리셋 ─────────────────────────────

    /// <summary>
    /// 소속 MazeArea 참조를 주입받아 에이전트를 초기화합니다.
    /// </summary>
    /// <param name="mazeArea">에이전트가 소속된 MazeArea 인스턴스</param>
    public virtual void InitializeAgent(MazeArea mazeArea)
    {
        area = mazeArea;
        EnsureReferences();
    }

    /// <summary>
    /// ML-Agents 초기화 라이프사이클 메서드입니다.
    /// </summary>
    public override void Initialize()
    {
        EnsureReferences();
    }

    /// <summary>
    /// 필수 컴포넌트(Rigidbody, BehaviorParameters, MazeArea)를 탐색하고 물리 제약 및 초기 시작 자세를 설정합니다.
    /// </summary>
    protected virtual void EnsureReferences()
    {
        if (rb == null)
        {
            rb = GetComponent<Rigidbody>();
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;
            rb.isKinematic = false;
        }

        if (behaviorParameters == null) behaviorParameters = GetComponent<BehaviorParameters>();
        if (area == null) area = GetComponentInParent<MazeArea>();
        if (cachedColliders == null || cachedColliders.Length == 0)
        {
            cachedColliders = GetComponentsInChildren<Collider>(true);
        }

        // 시작 좌표와 회전은 최초 런타임에 1회만 확정 기록 (재초기화 시 덮어쓰기 방지)
        initialPose.CaptureOnce(transform);
    }

    public override void OnEpisodeBegin()
    {
        area.OnAgentEpisodeBegin(this);
    }

    /// <summary>
    /// 에피소드 리셋 시 에이전트의 위치, 물리 속도 및 상자 상호작용 상태를 초기화합니다.
    /// </summary>
    public virtual void ResetPosition()
    {
        EnsureReferences();
        ReleaseActiveBox();

        latchedTapAction = BoxActionNone;
        lastPolicyBoxAction = BoxActionNone;

        if (!rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        initialPose.Restore(transform, rb);
    }

    // ───────────────────────────── 액션 실행 파이프라인 ─────────────────────────────

    /// <summary>
    /// 신경망 또는 Heuristic으로부터 전달된 액션 버퍼를 수신하여 이동, 상자 상호작용, 보상 성형을 실행합니다.
    /// </summary>
    /// <param name="actions">수신된 액션 버퍼 (ContinuousActions, DiscreteActions)</param>
    public override void OnActionReceived(ActionBuffers actions)
    {
        ProcessBoxInteraction(actions);
        ApplyMovement(actions);
        ApplyRewardShaping();
        AddReward(DefaultTimeStepPenalty);
    }

    /// <summary>
    /// 연속 액션(ContinuousActions: X, Z)을 물리적 속도(linearVelocity)로 변환하여 적용합니다.
    /// 상자 잠금 해제 채널링 중에는 제자리 정지합니다.
    /// </summary>
    /// <param name="actions">액션 버퍼</param>
    private void ApplyMovement(ActionBuffers actions)
    {
        if (rb.isKinematic) return;

        var continuous = actions.ContinuousActions;
        Vector3 moveInput = new Vector3(continuous[0], 0f, continuous[1]);
        if (moveInput.sqrMagnitude > 1f) moveInput.Normalize();

        // 상자 잠금 해제 중에는 움직이지 못하도록 속도 0 적용
        float speed = IsChannelingBox() ? 0f : moveSpeed;
        rb.linearVelocity = new Vector3(moveInput.x * speed, rb.linearVelocity.y, moveInput.z * speed);
    }

    /// <summary>
    /// 수신된 이산 액션을 해석하여 상자 상호작용을 처리합니다.
    /// </summary>
    /// <param name="actions">액션 버퍼</param>
    private void ProcessBoxInteraction(ActionBuffers actions)
    {
        int boxAction = ReadPolicyBoxAction(actions);
        HandleBoxInteraction(boxAction);
    }

    /// <summary>
    /// 이산 액션 브랜치로부터 상자 상호작용 액션을 읽어옵니다.
    /// 연속 액션 반복 실행 환경에서 0 -> 1 로 전환되는 첫 프레임(Rising edge)에만 1회성 트리거로 해석합니다.
    /// </summary>
    /// <param name="actions">액션 버퍼</param>
    /// <returns>유효 액션 ID (BoxActionNone 또는 BoxActionToggleGrab)</returns>
    private int ReadPolicyBoxAction(ActionBuffers actions)
    {
        int raw = actions.DiscreteActions.Length > 0 ? actions.DiscreteActions[0] : BoxActionNone;

        int effective = (raw == BoxActionToggleGrab && lastPolicyBoxAction != BoxActionToggleGrab)
            ? BoxActionToggleGrab
            : BoxActionNone;

        lastPolicyBoxAction = raw;
        return effective;
    }

    private void HandleBoxInteraction(int actionType)
    {
        RefreshActiveBox();
        if (actionType == BoxActionNone) return;

        // 1. 이미 상자를 운반 중인 경우: 상호작용 시 내려놓고 자동 잠금(Lock)
        if (IsCarryingBox)
        {
            ReleaseActiveBox();
            return;
        }

        // 2. 가장 가까운 상자 탐색 및 사거리/시야선 검증
        MovableBox box = area.GetClosestBox(transform.position);
        if (box == null || !box.IsInRange(transform.position))
        {
            ReleaseActiveBox();
            return;
        }

        activeBox = box;

        // 3. 잠기지 않은 상자: 잡아서 운반(Grab) 시작 / 잠긴 상자: 자동 채널링 잠금 해제 시작
        if (!box.IsLocked)
        {
            box.TryGrab(gameObject);
        }
        else
        {
            box.StartChannelUnlock(gameObject, MySoundSource);
        }
    }

    private void RefreshActiveBox()
    {
        if (activeBox != null && activeBox.CurrentCarrier != gameObject && activeBox.CurrentUnlocker != gameObject)
        {
            activeBox = null;
        }
    }

    private void ReleaseActiveBox()
    {
        if (activeBox == null) return;
        activeBox.StopGrab(gameObject);
        activeBox.CancelChannelUnlock(gameObject);
        activeBox = null;
    }

    private bool IsChannelingBox()
    {
        return activeBox != null && activeBox.IsLocked && activeBox.CurrentUnlocker == gameObject;
    }

    // ───────────────────────────── 관측 도우미 (Observations) ─────────────────────────────

    /// <summary>
    /// 대상 트랜스폼이 에이전트의 360도 시야 반경 및 시야선(Line of Sight, 벽에 의한 차폐 없음) 내에 있는지 검사합니다.
    /// </summary>
    /// <param name="target">검사 대상 트랜스폼</param>
    /// <param name="maxDistance">최대 시야 거리 (기본값: 25m)</param>
    /// <returns>시야선 내에 가려짐 없이 위치하면 true</returns>
    public bool IsTargetInSight(Transform target, float maxDistance = 25f)
    {
        if (target == null) return false;

        Vector3 origin = transform.position + Vector3.up * 0.5f;
        Vector3 targetPos = target.position + Vector3.up * 0.5f;
        if ((targetPos - origin).sqrMagnitude > maxDistance * maxDistance) return false;

        // Wall 레이어만 대상으로 Linecast 1회 수행 (버퍼 크기 제한 및 불필요한 콜라이더 순회 제거)
        return !Physics.Linecast(origin, targetPos, MazeArea.WallLayerMask, QueryTriggerInteraction.Ignore);
    }

    /// <summary>
    /// 에이전트가 현재 상자를 잡고 운반 중인지 여부(1차원)를 벡터 센서에 추가합니다.
    /// </summary>
    /// <param name="sensor">관측 데이터를 기록할 벡터 센서</param>
    protected void AddBoxCarryObservation(VectorSensor sensor)
    {
        sensor.AddObservation(IsCarryingBox ? 1.0f : 0.0f);
    }

    /// <summary>
    /// 상대방이 낸 소리 정보를 센서에 추가합니다. (상대 위치 3차원 + 잔여 시간 비율 1차원 = 총 4차원)
    /// </summary>
    protected void AddSoundObservation(VectorSensor sensor, SoundSource heardSource)
    {
        if (area.TryGetSound(heardSource, out Vector3 soundPos, out float remaining))
        {
            sensor.AddObservation((soundPos - transform.position) * ObservationPositionScale);
            sensor.AddObservation(remaining / Mathf.Max(0.01f, area.soundDuration));
        }
        else
        {
            sensor.AddObservation(Vector3.zero);
            sensor.AddObservation(0f);
        }
    }

    // ───────────────────────────── 수동 조작 (Heuristic) ─────────────────────────────

    private void Update()
    {
        if (!IsManualControlActive)
        {
            latchedTapAction = BoxActionNone;
            return;
        }

        var kb = Keyboard.current;
        if (kb == null) return;

        if ((useWasd && AnyKeyPressedThisFrame(kb, WasdInteractKeys))
            || (useArrows && AnyKeyPressedThisFrame(kb, ArrowInteractKeys)))
        {
            latchedTapAction = BoxActionToggleGrab;
        }
    }

    private static bool AnyKeyPressedThisFrame(Keyboard kb, Key[] keys)
    {
        for (int i = 0; i < keys.Length; i++)
        {
            if (kb[keys[i]].wasPressedThisFrame) return true;
        }
        return false;
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        Vector2 move = ReadKeyboardMovement();
        var continuous = actionsOut.ContinuousActions;
        continuous[0] = move.x;
        continuous[1] = move.y;

        var discrete = actionsOut.DiscreteActions;
        discrete[0] = latchedTapAction;
        latchedTapAction = BoxActionNone;
    }

    private Vector2 ReadKeyboardMovement()
    {
        var kb = Keyboard.current;
        if (kb == null) return Vector2.zero;

        float x = 0f;
        float z = 0f;

        if (useWasd)
        {
            x += Axis(kb.dKey, kb.aKey);
            z += Axis(kb.wKey, kb.sKey);
        }

        if (useArrows)
        {
            x += Axis(kb.rightArrowKey, kb.leftArrowKey);
            z += Axis(kb.upArrowKey, kb.downArrowKey);
        }

        return new Vector2(Mathf.Clamp(x, -1f, 1f), Mathf.Clamp(z, -1f, 1f));
    }

    private static float Axis(KeyControl positive, KeyControl negative)
    {
        return (positive.isPressed ? 1f : 0f) - (negative.isPressed ? 1f : 0f);
    }
}
