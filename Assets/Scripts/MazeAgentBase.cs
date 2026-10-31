using System;
using UnityEngine;
using Unity.MLAgents;

/// <summary>
/// 미로 술래잡기 환경의 공통 에이전트 기반 클래스 (EnemyAgent, PlayerAgent의 부모)
/// 물리 슬라이딩 이동, 상자 상호작용, 초기화 및 리셋 등 공통 로직을 캡슐화합니다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public abstract class MazeAgentBase : Agent
{
    [Header("이동 설정")]
    [Tooltip("에이전트 이동 속도")]
    public float moveSpeed = 5.0f;

    protected Rigidbody rb;
    protected MazeArea area;
    protected Vector3 initialPosition;
    protected Quaternion initialRotation;

    protected MovableBox activeBox;
    protected bool isHoldingInteractKey;
    protected bool pendingLockInput;

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
        if (rb != null)
        {
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;
            rb.isKinematic = false;
        }

        if (area == null) area = GetComponentInParent<MazeArea>();
        initialPosition = transform.position;
        initialRotation = transform.rotation;
    }

    public virtual void ResetPosition()
    {
        ReleaseActiveBox();
        transform.position = initialPosition;
        transform.rotation = initialRotation;

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    /// <summary>
    /// SweepTest 기반 물리 이동: 벽이나 상자를 뚫지 않고 표면을 따라 부드럽게 미끄러지도록 처리합니다.
    /// </summary>
    protected void MoveAgent(Vector3 moveInput, float currentSpeed, float dt)
    {
        if (rb == null || currentSpeed <= 0f) return;
        if (moveInput.sqrMagnitude > 1f) moveInput.Normalize();

        Vector3 delta = moveInput * currentSpeed * dt;
        float dist = delta.magnitude;
        if (dist <= 1e-5f) return;

        Vector3 dir = delta / dist;
        if (rb.SweepTest(dir, out RaycastHit hit, dist, QueryTriggerInteraction.Ignore))
        {
            float allowedDist = Mathf.Max(0f, hit.distance - 0.015f);
            Vector3 safeMove = dir * allowedDist;

            Vector3 normal = hit.normal;
            normal.y = 0f;
            if (normal.sqrMagnitude > 0.01f)
            {
                normal.Normalize();
                Vector3 slide = Vector3.ProjectOnPlane(dir, normal);
                float remaining = dist - allowedDist;
                if (slide.sqrMagnitude > 0.01f && remaining > 0.001f)
                {
                    slide.Normalize();
                    if (!rb.SweepTest(slide, out RaycastHit slideHit, remaining, QueryTriggerInteraction.Ignore))
                    {
                        safeMove += slide * remaining;
                    }
                    else
                    {
                        safeMove += slide * Mathf.Max(0f, slideHit.distance - 0.015f);
                    }
                }
            }
            rb.MovePosition(rb.position + safeMove);
        }
        else
        {
            rb.MovePosition(rb.position + delta);
        }
    }

    /// <summary>
    /// 상자 상호작용 공통 처리 (0: 없음, 1: 잡기 또는 잠금해제, 2: 고정)
    /// </summary>
    protected void HandleBoxInteraction(
        int actionType,
        SoundSource mySource,
        float dt,
        bool autoUnlockOpponentBox,
        Action onUnlockSuccess = null,
        Action onLockSuccess = null)
    {
        if (area == null) return;

        MovableBox box = area.GetClosestBox(transform.position);
        if (box == null || !box.IsInRange(transform.position, gameObject))
        {
            ReleaseActiveBox();
            return;
        }

        activeBox = box;

        if (actionType == 2 && !box.IsLocked)
        {
            if (box.TryLock(gameObject, mySource))
            {
                onLockSuccess?.Invoke();
            }
        }
        else if (actionType == 1 || (autoUnlockOpponentBox && box.IsLocked && box.LockedBy != mySource))
        {
            if (box.IsLocked)
            {
                if (box.TryChannelUnlock(gameObject, mySource, dt))
                {
                    onUnlockSuccess?.Invoke();
                }
            }
            else if (actionType == 1)
            {
                box.TryGrab(gameObject);
            }
        }
        else
        {
            ReleaseActiveBox();
        }
    }

    protected void ReleaseActiveBox()
    {
        if (activeBox == null) return;
        activeBox.StopGrab(gameObject);
        activeBox.CancelChannelUnlock(gameObject);
        activeBox = null;
    }

    protected bool IsChannelingBox()
    {
        return activeBox != null && activeBox.IsLocked && activeBox.CurrentUnlocker == gameObject;
    }

    /// <summary>
    /// 키보드 수동 입력(Heuristic)을 읽는 공통 도우미
    /// </summary>
    protected Vector2 ReadKeyboardMovement(bool checkWasd = true, bool checkArrows = true, bool checkIjkl = false)
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return Vector2.zero;

        float x = 0f;
        float z = 0f;

        if (checkWasd)
        {
            if (kb.dKey.isPressed) x += 1f;
            if (kb.aKey.isPressed) x -= 1f;
            if (kb.wKey.isPressed) z += 1f;
            if (kb.sKey.isPressed) z -= 1f;
        }

        if (checkArrows)
        {
            if (kb.rightArrowKey.isPressed) x += 1f;
            if (kb.leftArrowKey.isPressed) x -= 1f;
            if (kb.upArrowKey.isPressed) z += 1f;
            if (kb.downArrowKey.isPressed) z -= 1f;
        }

        if (checkIjkl)
        {
            if (kb.lKey.isPressed) x += 1f;
            if (kb.jKey.isPressed) x -= 1f;
            if (kb.iKey.isPressed) z += 1f;
            if (kb.kKey.isPressed) z -= 1f;
        }

        return new Vector2(Mathf.Clamp(x, -1f, 1f), Mathf.Clamp(z, -1f, 1f));
    }
}
