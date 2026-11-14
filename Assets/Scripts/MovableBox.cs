using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 에이전트(적 또는 플레이어)가 잡고 운반(Grab), 고정(Lock), 채널링 잠금 해제(Unlock)할 수 있는 상자 컴포넌트입니다.
/// 상자는 물리 시뮬레이션 영향 없이 움직이는 Kinematic Rigidbody이며, 운반 중에는 운반자를 따라 이동합니다.
/// </summary>
[SelectionBase]
[RequireComponent(typeof(BoxCollider))]
[RequireComponent(typeof(Rigidbody))]
public class MovableBox : MonoBehaviour
{
    /// <summary>운반 중 고속 이동 시 상자가 손에서 떨어지지 않도록 허용하는 추가 거리 여유치</summary>
    private const float GrabRangeMargin = 2.5f;

    /// <summary>잠금 해제 채널링 진행 중 주기적으로 소리를 발생시키는 간격 (초)</summary>
    private const float UnlockSoundInterval = 0.5f;

    /// <summary>열린(미잠금) 상자의 태그</summary>
    private const string UnlockedBoxTag = "Box";

    /// <summary>고정(잠금) 상태 상자의 태그 (RayPerceptionSensor가 시야로 식별)</summary>
    private const string LockedBoxTag = "LockedBox";

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly RaycastHit[] LineOfSightHits = new RaycastHit[16];

    [Header("상호작용 설정")]
    [Tooltip("고정된 상자를 해제하는 데 걸리는 시간 (초)")]
    public float unlockDuration = 3.0f;

    [Tooltip("상자 표면으로부터 상호작용 가능한 최대 거리 (2.5m)")]
    public float interactionDistance = 2.5f;

    [Tooltip("에피소드 시작 시 기본 고정 여부")]
    public bool startLocked = false;

    [Header("쿨타임 설정")]
    [Tooltip("상자가 잠긴 직후 잠금 해제를 시도할 수 있을 때까지의 쿨타임 (초)")]
    public float unlockCooldown = 1.0f;

    [Tooltip("상자가 잠금 해제된 직후 다시 잠글 수 있을 때까지의 쿨타임 (초)")]
    public float relockCooldown = 1.0f;

    [Header("시각 피드백 색상")]
    [Tooltip("미잠금 일반 상태 색상")]
    public Color unlockedColor = new Color(0.75f, 0.58f, 0.36f, 1f);

    [Tooltip("운반(Grab) 중일 때 색상")]
    public Color grabbedColor = new Color(0.35f, 0.85f, 0.95f, 1f);

    [Tooltip("고정(Lock) 상태 색상")]
    public Color lockedColor = new Color(0.9f, 0.25f, 0.2f, 1f);

    [Tooltip("잠금 해제 채널링 진행 중 색상")]
    public Color unlockingColor = new Color(1.0f, 0.85f, 0.2f, 1f);

    [Header("실시간 상태 (인스펙터 모니터링용)")]
    [SerializeField] private bool isLocked;
    [SerializeField] private float unlockProgress;
    [SerializeField] private GameObject currentCarrier;
    [SerializeField] private GameObject currentUnlocker;
    [SerializeField] private SoundSource lockedBy = SoundSource.None;
    private SoundSource unlockerSource = SoundSource.None;

    /// <summary>현재 상자가 고정(잠금) 상태인지 여부</summary>
    public bool IsLocked => isLocked;

    /// <summary>잠금 해제 진행도 정규화 비율 (0.0 ~ 1.0)</summary>
    public float UnlockNormalized => unlockDuration > 0f ? Mathf.Clamp01(unlockProgress / unlockDuration) : 0f;

    /// <summary>현재 상자를 잡고 운반 중인 게임 오브젝트</summary>
    public GameObject CurrentCarrier => currentCarrier;

    /// <summary>현재 상자를 잠금 해제 채널링 중인 게임 오브젝트</summary>
    public GameObject CurrentUnlocker => currentUnlocker;

    /// <summary>상자를 잠근 주체 (SoundSource.Player 또는 SoundSource.Enemy)</summary>
    public SoundSource LockedBy => lockedBy;

    private MazeArea area;
    private Rigidbody rb;
    private BoxCollider boxCollider;
    private Renderer[] visualRenderers;
    private MaterialPropertyBlock propBlock;

    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private bool initialPoseCaptured;

    private Vector3 grabOffset;
    private float unlockSoundTimer;
    private float unlockAvailableTime;
    private float relockAvailableTime;

    /// <summary>
    /// 컴포넌트 초기화 및 물리/콜라이더 설정을 구성합니다.
    /// </summary>
    private void Awake()
    {
        EnsureComponents();
    }

    /// <summary>
    /// 소속 MazeArea 참조를 주입받아 초기화합니다.
    /// </summary>
    /// <param name="mazeArea">상자가 속한 MazeArea 인스턴스</param>
    public void Initialize(MazeArea mazeArea)
    {
        area = mazeArea;
        EnsureComponents();
    }

    /// <summary>
    /// 필수 물리 컴포넌트(BoxCollider, Rigidbody)를 확인하고 키네마틱 설정 및 초기 좌표를 캐싱합니다.
    /// </summary>
    private void EnsureComponents()
    {
        if (boxCollider == null) boxCollider = GetComponent<BoxCollider>();
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (area == null) area = GetComponentInParent<MazeArea>();

        if (boxCollider != null)
        {
            boxCollider.isTrigger = false;
            boxCollider.enabled = true;
        }

        if (rb != null)
        {
            rb.useGravity = false;
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        // 초기 위치는 런타임 시작 시 1회만 확정 기록
        if (!initialPoseCaptured)
        {
            initialPosition = transform.position;
            initialRotation = transform.rotation;
            if (Application.isPlaying) initialPoseCaptured = true;
        }

        CacheRenderers();
        UpdateBoxTag();
        UpdateVisualState();
    }

    /// <summary>
    /// 상자의 잠금 상태에 따라 태그를 Box 또는 LockedBox로 갱신하여,
    /// 레이캐스트 센서(RayPerceptionSensor)가 시각적으로 상태를 식별할 수 있도록 합니다.
    /// </summary>
    private void UpdateBoxTag()
    {
        gameObject.tag = isLocked ? LockedBoxTag : UnlockedBoxTag;
    }

    /// <summary>
    /// 시각적 피드백 표현을 위해 메시가 존재하는 자식 렌더러 컴포넌트를 캐싱합니다.
    /// </summary>
    private void CacheRenderers()
    {
        Renderer[] all = GetComponentsInChildren<Renderer>(true);
        List<Renderer> valid = new List<Renderer>();
        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r == null) continue;
            MeshFilter mf = r.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh == null) continue;
            valid.Add(r);
        }
        visualRenderers = valid.ToArray();
    }

    /// <summary>
    /// 물리 틱마다 운반 이동 추적 및 잠금 해제 자동 채널링을 수행합니다.
    /// </summary>
    private void FixedUpdate()
    {
        // 1. 운반(Grab) 이동 처리
        if (currentCarrier != null)
        {
            if (isLocked
                || !currentCarrier.activeInHierarchy
                || GetSurfaceDistance(currentCarrier.transform.position) > interactionDistance + GrabRangeMargin)
            {
                StopGrab();
            }
            else
            {
                Vector3 desiredPos = currentCarrier.transform.position + grabOffset;
                desiredPos.y = initialPosition.y;
                rb.MovePosition(desiredPos);
            }
        }

        // 2. 잠금 해제 자동 채널링 진행 (1회 입력 시 자동 채널링 유지)
        if (isLocked && currentUnlocker != null)
        {
            if (!currentUnlocker.activeInHierarchy || !IsInRange(currentUnlocker.transform.position, currentUnlocker))
            {
                CancelChannelUnlock();
            }
            else
            {
                unlockProgress += Time.fixedDeltaTime;
                unlockSoundTimer -= Time.fixedDeltaTime;
                if (unlockSoundTimer <= 0f)
                {
                    if (area != null) area.EmitSound(transform.position, unlockerSource, "BoxUnlockChanneling");
                    unlockSoundTimer = UnlockSoundInterval;
                }

                if (unlockProgress >= unlockDuration)
                {
                    CompleteUnlock();
                }
                else
                {
                    UpdateVisualState();
                }
            }
        }
    }

    /// <summary>
    /// 에피소드 리셋 시 상자의 위치, 잠금 상태, 채널링, 쿨타임 및 충돌 무시 관계를 초기 상태로 복구합니다.
    /// </summary>
    public void ResetBox()
    {
        if (rb == null) EnsureComponents();

        StopGrab(null, autoLock: false);
        CancelChannelUnlock();

        // 에피소드 리셋 시 남아있을 수 있는 에이전트 간의 충돌 무시 상태 완벽 해제
        if (area != null)
        {
            if (area.player != null) SetIgnoreCollision(area.player.gameObject, false);
            if (area.enemy != null) SetIgnoreCollision(area.enemy.gameObject, false);
        }

        isLocked = startLocked;
        lockedBy = SoundSource.None;
        unlockerSource = SoundSource.None;
        unlockProgress = 0f;
        unlockAvailableTime = 0f;
        relockAvailableTime = 0f;

        rb.position = initialPosition;
        rb.rotation = initialRotation;
        transform.SetPositionAndRotation(initialPosition, initialRotation);

        UpdateBoxTag();
        UpdateVisualState();
    }

    /// <summary>
    /// 주어진 좌표로부터 상자 콜라이더 표면까지의 2D 평면 거리를 계산합니다.
    /// </summary>
    /// <param name="actorPosition">에이전트 월드 좌표</param>
    /// <returns>상자 표면까지의 최단 평면 거리</returns>
    public float GetSurfaceDistance(Vector3 actorPosition)
    {
        Vector3 flatActor = new Vector3(actorPosition.x, transform.position.y, actorPosition.z);
        Vector3 closest = boxCollider != null ? boxCollider.ClosestPoint(flatActor) : transform.position;
        closest.y = transform.position.y;
        return Vector3.Distance(flatActor, closest);
    }

    /// <summary>
    /// 에이전트와 상자 사이에 벽(Wall 태그) 장애물이 가로막고 있는지 검사합니다.
    /// </summary>
    /// <param name="actorPosition">에이전트 위치</param>
    /// <param name="actor">에이전트 오브젝트 (자체 콜라이더 무시용)</param>
    /// <returns>벽으로 가려지지 않았으면 true</returns>
    public bool HasLineOfSight(Vector3 actorPosition, GameObject actor = null)
    {
        Vector3 origin = actorPosition + Vector3.up * 0.5f;
        Vector3 targetPoint = boxCollider != null ? boxCollider.ClosestPoint(origin) : transform.position;
        targetPoint.y = origin.y;

        Vector3 rayDir = targetPoint - origin;
        float dist = rayDir.magnitude;
        if (dist <= 0.15f) return true;

        int hitCount = Physics.RaycastNonAlloc(origin, rayDir / dist, LineOfSightHits,
            Mathf.Max(0.01f, dist - 0.05f), ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            Collider col = LineOfSightHits[i].collider;
            if (col == boxCollider || col.transform.IsChildOf(transform)) continue;
            if (actor != null && col.transform.IsChildOf(actor.transform)) continue;
            if (col.GetComponent<MovableBox>() != null) continue;

            if (col.CompareTag("Wall")) return false;
        }
        return true;
    }

    /// <summary>
    /// 에이전트가 상호작용 사거리(2.5m) 이내에 있고 벽에 가려지지 않았는지 검사합니다.
    /// </summary>
    /// <param name="actorPosition">에이전트 위치</param>
    /// <param name="actor">에이전트 게임 오브젝트</param>
    /// <returns>상호작용 가능 범위 내이면 true</returns>
    public bool IsInRange(Vector3 actorPosition, GameObject actor = null)
    {
        return GetSurfaceDistance(actorPosition) <= interactionDistance && HasLineOfSight(actorPosition, actor);
    }

    /// <summary>
    /// 에이전트가 상자를 잡고 운반을 시작하도록 시도합니다.
    /// </summary>
    /// <param name="actor">상자를 잡으려는 에이전트</param>
    /// <returns>잡기 성공 시 true</returns>
    public bool TryGrab(GameObject actor)
    {
        if (actor == null || isLocked || (currentCarrier != null && currentCarrier != actor)) return false;
        if (!IsInRange(actor.transform.position, actor)) return false;
        if (currentCarrier == actor) return true;

        currentCarrier = actor;
        grabOffset = transform.position - actor.transform.position;
        grabOffset.y = 0f;

        SetIgnoreCollision(actor, true);
        UpdateVisualState();
        return true;
    }

    /// <summary>
    /// 운반 중인 상자를 내려놓습니다. autoLock이 true이면 내려놓는 즉시 상자를 고정(Lock)합니다.
    /// </summary>
    /// <param name="actor">운반을 해제하는 주체 (null이면 강제 해제)</param>
    /// <param name="autoLock">내려놓은 뒤 자동으로 고정할지 여부</param>
    public void StopGrab(GameObject actor = null, bool autoLock = true)
    {
        if (currentCarrier == null)
        {
            if (actor != null) SetIgnoreCollision(actor, false);
            return;
        }

        if (actor != null && currentCarrier != actor)
        {
            SetIgnoreCollision(actor, false);
            return;
        }

        GameObject carrier = currentCarrier;
        currentCarrier = null;
        SetIgnoreCollision(carrier, false);
        UpdateVisualState();

        if (autoLock && !isLocked)
        {
            SoundSource source = SoundSource.None;
            var agent = carrier.GetComponent<MazeAgentBase>();
            if (agent != null) source = agent.SoundSource;
            TryLock(carrier, source, skipDistanceCheck: true, skipCooldown: true);
        }
    }

    /// <summary>
    /// 상자를 고정(잠금) 상태로 전환합니다.
    /// </summary>
    /// <param name="actor">잠금을 시도하는 에이전트</param>
    /// <param name="source">잠금을 실행한 에이전트의 소리 주체</param>
    /// <param name="skipDistanceCheck">사거리 검사 생략 여부 (내려놓기 연계 시)</param>
    /// <param name="skipCooldown">쿨타임 검사 생략 여부</param>
    /// <returns>고정 성공 시 true</returns>
    public bool TryLock(GameObject actor, SoundSource source, bool skipDistanceCheck = false, bool skipCooldown = false)
    {
        if (actor == null || isLocked) return false;
        if (!skipDistanceCheck && !IsInRange(actor.transform.position, actor)) return false;
        if (!skipCooldown && Time.time < relockAvailableTime) return false;

        if (currentCarrier != null)
        {
            StopGrab(null, autoLock: false);
        }
        CancelChannelUnlock();

        isLocked = true;
        lockedBy = source;
        unlockAvailableTime = Time.time + unlockCooldown;

        if (area != null) area.EmitSound(transform.position, source, "BoxLock");

        UpdateBoxTag();
        UpdateVisualState();
        return true;
    }

    /// <summary>
    /// 1회 입력 시 호출되어 잠금 해제 채널링을 자동으로 시작합니다.
    /// </summary>
    /// <param name="actor">해제를 시도하는 에이전트</param>
    /// <param name="source">해제자의 소리 주체</param>
    /// <returns>채널링 시작 성공 시 true</returns>
    public bool StartChannelUnlock(GameObject actor, SoundSource source)
    {
        if (actor == null || !isLocked) return false;
        if (Time.time < unlockAvailableTime) return false;
        if (!IsInRange(actor.transform.position, actor)) return false;
        if (currentUnlocker != null && currentUnlocker != actor) return false;

        if (currentUnlocker == actor) return true; // 이미 채널링 중

        currentUnlocker = actor;
        unlockerSource = source;
        unlockProgress = 0f;
        unlockSoundTimer = 0f;

        if (area != null) area.EmitSound(transform.position, source, "BoxUnlockStart");
        UpdateVisualState();
        return true;
    }

    /// <summary>
    /// 채널링 완료 시 상자 잠금을 해제하고 완료 소리 전파 및 에이전트 알림을 수행합니다.
    /// </summary>
    private void CompleteUnlock()
    {
        SoundSource finishedSource = unlockerSource;

        isLocked = false;
        lockedBy = SoundSource.None;
        unlockerSource = SoundSource.None;
        unlockProgress = 0f;
        currentUnlocker = null;
        relockAvailableTime = Time.time + relockCooldown;

        if (area != null) area.EmitSound(transform.position, finishedSource, "BoxUnlockComplete");

        UpdateBoxTag();
        UpdateVisualState();
    }

    /// <summary>
    /// 진행 중이던 잠금 해제 채널링을 취소하고 진행 시간을 0으로 리셋합니다.
    /// </summary>
    /// <param name="actor">취소를 요청한 주체 (null이면 강제 취소)</param>
    public void CancelChannelUnlock(GameObject actor = null)
    {
        if (actor != null && currentUnlocker != actor) return;
        if (currentUnlocker == null && unlockProgress <= 0f) return;

        currentUnlocker = null;
        unlockerSource = SoundSource.None;
        unlockProgress = 0f;
        UpdateVisualState();
    }

    /// <summary>
    /// 상자와 에이전트 간의 물리 콜라이더 충돌 무시 설정을 켜거나 끕니다. (운반 중 겹침 방지 및 튕김 억제)
    /// </summary>
    /// <param name="actor">대상 에이전트 게임 오브젝트</param>
    /// <param name="ignore">충돌 무시 여부</param>
    private void SetIgnoreCollision(GameObject actor, bool ignore)
    {
        if (actor == null || boxCollider == null) return;

        foreach (Collider actorCollider in actor.GetComponentsInChildren<Collider>())
        {
            Physics.IgnoreCollision(actorCollider, boxCollider, ignore);
        }
    }

    /// <summary>
    /// 현재 상자의 잠금/운반/채널링 상태에 부합하는 베이스 색상을 계산합니다.
    /// </summary>
    /// <returns>적용할 기본 색상</returns>
    private Color GetCurrentColor()
    {
        if (isLocked)
        {
            return (currentUnlocker != null && unlockProgress > 0f)
                ? Color.Lerp(lockedColor, unlockingColor, UnlockNormalized)
                : lockedColor;
        }

        return currentCarrier != null ? grabbedColor : unlockedColor;
    }

    /// <summary>
    /// 현재 상자 상태에 따른 발광(Emission) 색상을 계산합니다.
    /// </summary>
    /// <returns>적용할 이미션 색상</returns>
    private Color GetCurrentEmissionColor()
    {
        if (isLocked)
        {
            Color c = (currentUnlocker != null && unlockProgress > 0f)
                ? Color.Lerp(lockedColor, unlockingColor, UnlockNormalized)
                : lockedColor;
            return c * 0.45f;
        }

        if (currentCarrier != null)
        {
            return grabbedColor * 0.45f;
        }

        return Color.black;
    }

    /// <summary>
    /// MaterialPropertyBlock을 통해 렌더러의 색상 및 발광을 상태에 맞게 실시간 갱신합니다.
    /// </summary>
    private void UpdateVisualState()
    {
        if (visualRenderers == null || visualRenderers.Length == 0)
        {
            CacheRenderers();
            if (visualRenderers == null || visualRenderers.Length == 0) return;
        }

        if (propBlock == null) propBlock = new MaterialPropertyBlock();

        Color targetColor = GetCurrentColor();
        Color emissionColor = GetCurrentEmissionColor();

        for (int i = 0; i < visualRenderers.Length; i++)
        {
            Renderer r = visualRenderers[i];
            if (r == null) continue;

            r.GetPropertyBlock(propBlock);
            propBlock.SetColor(BaseColorId, targetColor);
            propBlock.SetColor(ColorId, targetColor);
            propBlock.SetColor(EmissionColorId, emissionColor);
            r.SetPropertyBlock(propBlock);
        }
    }

    /// <summary>
    /// 씬 뷰에서 상자의 잠금/운반 상태를 가시화하기 위한 기즈모를 렌더링합니다.
    /// </summary>
    private void OnDrawGizmos()
    {
        if (isLocked)
        {
            Gizmos.color = currentUnlocker != null ? unlockingColor : lockedColor;
            Gizmos.DrawWireCube(transform.position, transform.localScale * 1.05f);
        }
        else if (currentCarrier != null)
        {
            Gizmos.color = grabbedColor;
            Gizmos.DrawWireCube(transform.position, transform.localScale * 1.05f);
        }
    }
}
