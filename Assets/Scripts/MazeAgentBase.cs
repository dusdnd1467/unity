using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;

/// <summary>
/// 미로 술래잡기 환경의 공통 에이전트 기반 클래스 (EnemyAgent, PlayerAgent의 부모)
/// 물리 속도 기반 이동, 상자 상호작용, 수동 조작(Heuristic), 초기화 및 리셋 등 공통 로직을 캡슐화합니다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public abstract class MazeAgentBase : Agent
{
    /// <summary>위치 관측값 정규화 계수 (월드 좌표 → 대략 [-1, 1])</summary>
    protected const float ObservationPositionScale = 0.05f;

    /// <summary>공통 상자 해제 보상</summary>
    protected const float DefaultBoxUnlockReward = 0.05f;

    /// <summary>공통 타임스텝 페널티 (빠른 클리어 유도)</summary>
    protected const float DefaultTimeStepPenalty = -0.0005f;

    // 상자 상호작용 액션 ID (이산 액션 0번 브랜치 값)
    private const int BoxActionNone = 0;        // 현재 상태 유지
    private const int BoxActionToggleGrab = 1;  // 잡기 / 내려놓기 토글 (1회 입력)
    private const int BoxActionLock = 2;        // 고정 (1회 입력)
    private const int BoxActionUnlockHold = 3;  // 잠금 해제 채널링 (지속 홀드)

    // 수동 조작 키 배치 (WASD 체계와 방향키 체계 분리)
    private static readonly Key[] WasdLockKeys = { Key.F, Key.V };
    private static readonly Key[] WasdInteractKeys = { Key.E, Key.C };
    private static readonly Key[] ArrowLockKeys = { Key.RightShift, Key.NumpadEnter, Key.O, Key.L };
    private static readonly Key[] ArrowInteractKeys = { Key.Space, Key.RightCtrl, Key.Numpad0, Key.Enter, Key.U };

    [Header("이동 설정")]
    [Tooltip("에이전트 이동 속도")]
    public float moveSpeed = 5.0f;

    protected Rigidbody rb;
    protected MazeArea area;

    private BehaviorParameters behaviorParameters;
    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private bool initialPoseCaptured;

    private MovableBox activeBox;

    // 수동 조작 입력 상태 (Update에서 래치, OnActionReceived에서 소비)
    private int latchedTapAction;
    private bool manualInteractHeld;

    // RL 정책의 이전 상자 액션 (연속 호출 시 1회성 액션의 중복 방지)
    private int lastPolicyBoxAction;

    /// <summary>WASD 수동 조작 사용 여부</summary>
    public abstract bool UseWasd { get; }

    /// <summary>방향키 수동 조작 사용 여부</summary>
    public abstract bool UseArrows { get; }

    /// <summary>상자 조작 시 발생하는 소리의 주체</summary>
    protected abstract SoundSource MySoundSource { get; }

    /// <summary>상대가 고정한 상자에 접근하면 자동으로 해제를 시도할지 여부 (RL 제어 시에만 적용)</summary>
    protected abstract bool AutoUnlockOpponentBox { get; }

    /// <summary>거리 기반 보상 등 에이전트별 보상 성형 (매 액션마다 호출)</summary>
    protected abstract void ApplyRewardShaping();

    /// <summary>상자 고정 성공 시 호출 (보상 지급 등)</summary>
    protected virtual void OnBoxLocked() { }

    /// <summary>상자 해제 성공 시 기본 보상 지급 (필요 시 override 가능)</summary>
    protected virtual void OnBoxUnlocked()
    {
        AddReward(DefaultBoxUnlockReward);
    }

    /// <summary>모든 에이전트의 위치 리셋이 끝난 뒤 보상 계산용 이전 거리 값을 다시 초기화합니다.</summary>
    public virtual void RefreshTracking() { }

    /// <summary>수동 조작(Heuristic) 모드 여부</summary>
    protected bool IsHeuristicMode => behaviorParameters != null && behaviorParameters.IsInHeuristicMode();

    private bool IsManualControlActive => (UseWasd || UseArrows) && IsHeuristicMode;

    // ───────────────────────────── 초기화 / 리셋 ─────────────────────────────

    public virtual void InitializeAgent(MazeArea mazeArea)
    {
        area = mazeArea;
        EnsureReferences();
    }

    public override void Initialize()
    {
        EnsureReferences();
    }

    protected virtual void EnsureReferences()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (behaviorParameters == null) behaviorParameters = GetComponent<BehaviorParameters>();

        if (rb != null)
        {
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;
            rb.isKinematic = false;
        }

        if (area == null) area = GetComponentInParent<MazeArea>();

        // 시작 자세는 최초 1회만 기록 (재초기화 시 덮어쓰지 않도록 보호)
        if (!initialPoseCaptured)
        {
            initialPosition = transform.position;
            initialRotation = transform.rotation;
            initialPoseCaptured = true;
        }
    }

    public override void OnEpisodeBegin()
    {
        if (area != null)
        {
            area.ResetArea();
        }
        else
        {
            ResetPosition();
            RefreshTracking();
        }
    }

    public virtual void ResetPosition()
    {
        ReleaseActiveBox();

        latchedTapAction = BoxActionNone;
        manualInteractHeld = false;
        lastPolicyBoxAction = BoxActionNone;

        if (rb != null)
        {
            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            rb.position = initialPosition;
            rb.rotation = initialRotation;
        }
        transform.SetPositionAndRotation(initialPosition, initialRotation);
    }

    // ───────────────────────────── 액션 처리 ─────────────────────────────

    public override void OnActionReceived(ActionBuffers actions)
    {
        ProcessBoxInteraction(actions);
        ApplyMovement(actions);
        ApplyRewardShaping();
        AddReward(DefaultTimeStepPenalty);
    }

    /// <summary>물리 속도 기반 이동 (상자 해제 채널링 중에는 제자리 정지)</summary>
    private void ApplyMovement(ActionBuffers actions)
    {
        if (rb == null || rb.isKinematic) return;

        var continuous = actions.ContinuousActions;
        Vector3 moveInput = new Vector3(continuous[0], 0f, continuous[1]);
        if (moveInput.sqrMagnitude > 1f) moveInput.Normalize();

        float speed = IsChannelingBox() ? 0f : moveSpeed;
        rb.linearVelocity = new Vector3(moveInput.x * speed, rb.linearVelocity.y, moveInput.z * speed);
    }

    /// <summary>상자 상호작용 액션 실행</summary>
    private void ProcessBoxInteraction(ActionBuffers actions)
    {
        bool manual = IsHeuristicMode;
        int boxAction = manual ? ConsumeManualBoxAction() : ReadPolicyBoxAction(actions);
        bool autoUnlock = AutoUnlockOpponentBox && !manual;
        HandleBoxInteraction(boxAction, autoUnlock);
    }

    private int ReadPolicyBoxAction(ActionBuffers actions)
    {
        int raw = actions.DiscreteActions.Length > 0 ? actions.DiscreteActions[0] : BoxActionNone;

        // 잡기/고정은 1회성 액션: 같은 값이 연속 전달되는 동안에는 첫 프레임에만 1회 실행
        bool oneShot = raw == BoxActionToggleGrab || raw == BoxActionLock;
        int effective = (oneShot && raw == lastPolicyBoxAction) ? BoxActionNone : raw;

        lastPolicyBoxAction = raw;
        return effective;
    }

    private void HandleBoxInteraction(int actionType, bool autoUnlockOpponentBox)
    {
        if (area == null) return;

        // 1. 이미 상자를 운반(Grab) 중인 경우
        if (activeBox != null && activeBox.CurrentCarrier == gameObject)
        {
            if (actionType == BoxActionToggleGrab)
            {
                ReleaseActiveBox();
            }
            else if (actionType == BoxActionLock)
            {
                MovableBox carrying = activeBox;
                ReleaseActiveBox();
                if (carrying.TryLock(gameObject, MySoundSource)) OnBoxLocked();
            }
            return;
        }

        // 2. 가장 가까운 상호작용 가능 상자 탐색
        MovableBox box = area.GetClosestBox(transform.position);
        if (box == null || !box.IsInRange(transform.position, gameObject))
        {
            if (activeBox != null && activeBox.CurrentUnlocker == gameObject)
            {
                activeBox.CancelChannelUnlock(gameObject);
            }
            activeBox = null;
            return;
        }

        activeBox = box;

        if (!box.IsLocked)
        {
            if (actionType == BoxActionLock)
            {
                if (box.TryLock(gameObject, MySoundSource)) OnBoxLocked();
            }
            else if (actionType == BoxActionToggleGrab)
            {
                box.TryGrab(gameObject);
            }
            return;
        }

        // 3. 잠긴 상자 해제 채널링
        bool wantsUnlock = actionType == BoxActionToggleGrab
                           || actionType == BoxActionUnlockHold
                           || (autoUnlockOpponentBox && box.LockedBy != MySoundSource);

        if (wantsUnlock)
        {
            if (box.TryChannelUnlock(gameObject, MySoundSource, Time.fixedDeltaTime)) OnBoxUnlocked();
        }
        else if (box.CurrentUnlocker == gameObject)
        {
            box.CancelChannelUnlock(gameObject);
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

    // ───────────────────────────── 관측 도우미 ─────────────────────────────

    /// <summary>상대 에이전트 소리 관측 (상대 위치 3차원 + 남은 시간 비율 1차원 = 총 4차원)</summary>
    protected void AddSoundObservation(VectorSensor sensor, SoundSource heardSource)
    {
        Vector3 soundPos = Vector3.zero;
        float remaining = 0f;

        if (area != null)
        {
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

    private void Update()
    {
        manualInteractHeld = false;

        if (!IsManualControlActive)
        {
            latchedTapAction = BoxActionNone;
            return;
        }

        var kb = Keyboard.current;
        if (kb == null) return;

        if (UseWasd) PollManualKeys(kb, WasdLockKeys, WasdInteractKeys);
        if (UseArrows) PollManualKeys(kb, ArrowLockKeys, ArrowInteractKeys);
    }

    private void PollManualKeys(Keyboard kb, Key[] lockKeys, Key[] interactKeys)
    {
        if (AnyKey(kb, lockKeys, tapOnly: true))
        {
            latchedTapAction = BoxActionLock;
        }
        else if (latchedTapAction == BoxActionNone && AnyKey(kb, interactKeys, tapOnly: true))
        {
            latchedTapAction = BoxActionToggleGrab;
        }

        manualInteractHeld |= AnyKey(kb, interactKeys, tapOnly: false);
    }

    private int ConsumeManualBoxAction()
    {
        int action = latchedTapAction;
        latchedTapAction = BoxActionNone;

        if (action != BoxActionNone) return action;
        return manualInteractHeld ? BoxActionUnlockHold : BoxActionNone;
    }

    private static bool AnyKey(Keyboard kb, Key[] keys, bool tapOnly)
    {
        for (int i = 0; i < keys.Length; i++)
        {
            KeyControl control = kb[keys[i]];
            if (tapOnly ? control.wasPressedThisFrame : control.isPressed) return true;
        }
        return false;
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        Vector2 move = ReadKeyboardMovement();
        var continuous = actionsOut.ContinuousActions;
        continuous[0] = move.x;
        continuous[1] = move.y;
    }

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

    private static float Axis(KeyControl positive, KeyControl negative)
    {
        return (positive.isPressed ? 1f : 0f) - (negative.isPressed ? 1f : 0f);
    }
}
