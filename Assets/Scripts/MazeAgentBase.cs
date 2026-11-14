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
    protected const float DefaultTimeStepPenalty = -0.0005f;

    /// <summary>상자 상호작용 액션: 현재 상태 유지</summary>
    private const int BoxActionNone = 0;

    /// <summary>상자 상호작용 액션: 토글 (잡기 / 내려놓기 후 자동 잠금 / 잠금 해제 시작)</summary>
    private const int BoxActionToggleGrab = 1;

    /// <summary>WASD 조작 체계의 상자 상호작용 키 (E, C)</summary>
    private static readonly Key[] WasdInteractKeys = { Key.E, Key.C };

    /// <summary>방향키 조작 체계의 상자 상호작용 키 (Space, RightCtrl, Numpad0, Enter, U)</summary>
    private static readonly Key[] ArrowInteractKeys = { Key.Space, Key.RightCtrl, Key.Numpad0, Key.Enter, Key.U };

    /// <summary>시야선(Raycast) 검사용 캐싱 배열 (가비지 컬렉션 방지)</summary>
    private static readonly RaycastHit[] SightHits = new RaycastHit[16];

    [Header("이동 설정")]
    [Tooltip("에이전트 이동 속도")]
    public float moveSpeed = 5.0f;

    protected Rigidbody rb;
    protected MazeArea area;

    private BehaviorParameters behaviorParameters;
    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private bool initialPoseCaptured;

    /// <summary>현재 에이전트가 잡고 있거나 잠금 해제 채널링 중인 상자</summary>
    private MovableBox activeBox;

    /// <summary>수동 조작(Heuristic) 모드에서 Update 프레임 동안 눌린 키 입력을 보관하는 래치 변수</summary>
    private int latchedTapAction;

    /// <summary>강화학습 정책의 이전 상자 액션 (0->1 변경 순간인 Rising Edge 감지용)</summary>
    private int lastPolicyBoxAction;

    /// <summary>WASD 수동 조작 사용 여부</summary>
    public abstract bool UseWasd { get; }

    /// <summary>방향키 수동 조작 사용 여부</summary>
    public abstract bool UseArrows { get; }

    /// <summary>상자 조작 시 발생하는 소리의 주체</summary>
    public SoundSource SoundSource => MySoundSource;

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

        // 시작 좌표와 회전은 최초 런타임에 1회만 확정 기록 (재초기화 시 덮어쓰기 방지)
        if (!initialPoseCaptured)
        {
            initialPosition = transform.position;
            initialRotation = transform.rotation;
            if (Application.isPlaying) initialPoseCaptured = true;
        }
    }

    /// <summary>
    /// 에피소드 시작 시 호출되며, MazeArea를 통해 전체 환경의 동기화 리셋을 요청합니다.
    /// </summary>
    public override void OnEpisodeBegin()
    {
        area.OnAgentEpisodeBegin(this);
    }

    /// <summary>
    /// 에피소드 리셋 시 에이전트의 위치, 물리 속도 및 상자 상호작용 상태를 초기화합니다.
    /// </summary>
    public virtual void ResetPosition()
    {
        ReleaseActiveBox();

        latchedTapAction = BoxActionNone;
        lastPolicyBoxAction = BoxActionNone;

        if (!rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        rb.position = initialPosition;
        rb.rotation = initialRotation;
        transform.SetPositionAndRotation(initialPosition, initialRotation);
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
    /// 이동 방향으로 캐릭터 회전을 업데이트하며, 상자 잠금 해제 채널링 중에는 제자리 정지합니다.
    /// </summary>
    /// <param name="actions">액션 버퍼</param>
    private void ApplyMovement(ActionBuffers actions)
    {
        if (rb.isKinematic) return;

        var continuous = actions.ContinuousActions;
        Vector3 moveInput = new Vector3(continuous[0], 0f, continuous[1]);
        if (moveInput.sqrMagnitude > 1f) moveInput.Normalize();

        // 이동 입력이 있을 경우 전방(Forward) 회전 업데이트
        if (moveInput.sqrMagnitude > 0.001f)
        {
            transform.forward = moveInput;
        }

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

    /// <summary>
    /// 상자 상호작용 액션 타입에 따라 운반 중단/잠금, 신규 운반, 채널링 잠금 해제를 수행합니다.
    /// </summary>
    /// <param name="actionType">실행할 상자 액션 타입</param>
    private void HandleBoxInteraction(int actionType)
    {
        if (actionType == BoxActionNone) return;

        // 1. 이미 상자를 운반 중인 경우: 상호작용 시 내려놓고 자동 잠금(Lock)
        if (activeBox != null && activeBox.CurrentCarrier == gameObject)
        {
            ReleaseActiveBox();
            return;
        }

        // 2. 가장 가까운 상자 탐색 및 사거리/시야선 검증
        MovableBox box = area.GetClosestBox(transform.position);
        if (box == null || !box.IsInRange(transform.position, gameObject))
        {
            // 범위를 벗어났으면 기존 채널링 취소
            if (activeBox != null && activeBox.CurrentUnlocker == gameObject)
            {
                activeBox.CancelChannelUnlock(gameObject);
            }
            activeBox = null;
            return;
        }

        activeBox = box;

        // 3. 잠기지 않은 상자: 잡아서 운반(Grab) 시작
        if (!box.IsLocked)
        {
            box.TryGrab(gameObject);
            return;
        }

        // 4. 잠긴 상자: 1회 탭 시 즉시 자동 채널링 잠금 해제 모드로 진입
        box.StartChannelUnlock(gameObject, MySoundSource);
    }

    /// <summary>
    /// 현재 조작 중인 상자의 운반 및 잠금 해제 채널링을 안전하게 중단하고 참조를 해제합니다.
    /// </summary>
    private void ReleaseActiveBox()
    {
        if (activeBox == null) return;
        activeBox.StopGrab(gameObject);
        activeBox.CancelChannelUnlock(gameObject);
        activeBox = null;
    }

    /// <summary>
    /// 현재 에이전트가 상자를 잠금 해제하기 위해 제자리 채널링 중인지 여부를 반환합니다.
    /// </summary>
    /// <returns>채널링 중이면 true</returns>
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
        Vector3 dir = targetPos - origin;
        float dist = dir.magnitude;

        if (dist > maxDistance) return false;

        // 중간에 벽(Wall 태그)이 가로막고 있는지 RaycastNonAlloc으로 검사 (트리거 콜라이더 무시)
        int hitCount = Physics.RaycastNonAlloc(origin, dir.normalized, SightHits, dist, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hitCount; i++)
        {
            Collider col = SightHits[i].collider;
            if (col.transform == target || col.transform.IsChildOf(target)) continue;
            if (col.transform == transform || col.transform.IsChildOf(transform)) continue;
            if (col.CompareTag("Wall")) return false;
        }

        return true;
    }

    /// <summary>
    /// 상대방이 낸 소리 정보를 센서에 추가합니다. (상대 위치 3차원 + 잔여 시간 비율 1차원 = 총 4차원)
    /// </summary>
    /// <param name="sensor">관측 데이터를 기록할 벡터 센서</param>
    /// <param name="heardSource">듣고자 하는 상대 소리 주체</param>
    protected void AddSoundObservation(VectorSensor sensor, SoundSource heardSource)
    {
        Vector3 soundPos = Vector3.zero;
        float remaining = 0f;

        if (heardSource == SoundSource.Player)
        {
            soundPos = area.PlayerSoundPosition;
            remaining = area.PlayerSoundTimer;
        }
        else if (heardSource == SoundSource.Enemy)
        {
            soundPos = area.EnemySoundPosition;
            remaining = area.EnemySoundTimer;
        }

        if (remaining > 0f)
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

    /// <summary>
    /// Update 라이프사이클에서 매 프레임 키보드 상호작용 입력을 폴링하여 래치합니다.
    /// </summary>
    private void Update()
    {
        if (!IsManualControlActive)
        {
            latchedTapAction = BoxActionNone;
            return;
        }

        var kb = Keyboard.current;
        if (kb == null) return;

        if (UseWasd) PollManualKeys(kb, WasdInteractKeys);
        if (UseArrows) PollManualKeys(kb, ArrowInteractKeys);
    }

    /// <summary>
    /// 지정된 상호작용 키 배열 중 이번 프레임에 눌린 키가 있으면 래치에 토글 액션을 등록합니다.
    /// </summary>
    /// <param name="kb">키보드 인스턴스</param>
    /// <param name="interactKeys">모니터링할 키 목록</param>
    private void PollManualKeys(Keyboard kb, Key[] interactKeys)
    {
        if (AnyKey(kb, interactKeys, tapOnly: true))
        {
            latchedTapAction = BoxActionToggleGrab;
        }
    }

    /// <summary>
    /// 보관 중이던 수동 상호작용 키 입력을 반환하고 래치를 초기화합니다.
    /// </summary>
    /// <returns>소비된 액션 ID</returns>
    private int ConsumeManualBoxAction()
    {
        int action = latchedTapAction;
        latchedTapAction = BoxActionNone;
        return action;
    }

    /// <summary>
    /// 지정된 키 목록 중 하나라도 눌렸는지 검사하는 유틸리티 메서드입니다.
    /// </summary>
    /// <param name="kb">키보드 인스턴스</param>
    /// <param name="keys">키 목록</param>
    /// <param name="tapOnly">true이면 이번 프레임 첫 입력(wasPressedThisFrame), false면 누르고 있는지(isPressed)</param>
    /// <returns>키 입력 여부</returns>
    private static bool AnyKey(Keyboard kb, Key[] keys, bool tapOnly)
    {
        for (int i = 0; i < keys.Length; i++)
        {
            KeyControl control = kb[keys[i]];
            if (tapOnly ? control.wasPressedThisFrame : control.isPressed) return true;
        }
        return false;
    }

    /// <summary>
    /// 휴리스틱(수동 조작) 모드 실행 시 키보드 입력값을 ML-Agents 액션 버퍼로 전달합니다.
    /// </summary>
    /// <param name="actionsOut">출력 액션 버퍼 (ContinuousActions, DiscreteActions)</param>
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        Vector2 move = ReadKeyboardMovement();
        var continuous = actionsOut.ContinuousActions;
        continuous[0] = move.x;
        continuous[1] = move.y;

        var discrete = actionsOut.DiscreteActions;
        discrete[0] = ConsumeManualBoxAction();
    }

    /// <summary>
    /// 활성화된 수동 키보드 레이아웃(WASD 또는 방향키)으로부터 2D 이동 벡터(X, Z)를 읽어옵니다.
    /// </summary>
    /// <returns>정규화된 이동 벡터 (X, Y)</returns>
    private Vector2 ReadKeyboardMovement()
    {
        var kb = Keyboard.current;
        if (kb == null) return Vector2.zero;

        float x = 0f;
        float z = 0f;

        if (UseWasd)
        {
            x += Axis(kb.dKey, kb.aKey);
            z += Axis(kb.wKey, kb.sKey);
        }

        if (UseArrows)
        {
            x += Axis(kb.rightArrowKey, kb.leftArrowKey);
            z += Axis(kb.upArrowKey, kb.downArrowKey);
        }

        return new Vector2(Mathf.Clamp(x, -1f, 1f), Mathf.Clamp(z, -1f, 1f));
    }

    /// <summary>
    /// 두 개의 대립되는 키(양수, 음수) 입력을 축(-1 ~ +1) 값으로 계산합니다.
    /// </summary>
    /// <param name="positive">양수 방향 키</param>
    /// <param name="negative">음수 방향 키</param>
    /// <returns>-1, 0, +1 값</returns>
    private static float Axis(KeyControl positive, KeyControl negative)
    {
        return (positive.isPressed ? 1f : 0f) - (negative.isPressed ? 1f : 0f);
    }
}
