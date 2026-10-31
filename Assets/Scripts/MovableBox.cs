using UnityEngine;

/// <summary>
/// 에이전트(적 또는 플레이어)가 잡고 운반(Grab), 고정(Lock), 5초간 채널링 해제(Unlock)할 수 있는 상자 컴포넌트입니다.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
[RequireComponent(typeof(Rigidbody))]
public class MovableBox : MonoBehaviour
{
    [Header("상호작용 설정")]
    [Tooltip("고정된 상자를 해제하는 데 걸리는 시간 (초)")]
    public float unlockDuration = 5.0f;

    [Tooltip("상자 표면으로부터 상호작용 가능한 최대 거리")]
    public float interactionDistance = 1.8f;

    [Tooltip("에피소드 시작 시 기본 고정 여부")]
    public bool startLocked = false;

    [Header("시각 피드백 색상")]
    public Color unlockedColor = new Color(0.75f, 0.58f, 0.36f, 1f);
    public Color grabbedColor = new Color(0.35f, 0.85f, 0.95f, 1f);
    public Color lockedColor = new Color(0.9f, 0.25f, 0.2f, 1f);
    public Color unlockingColor = new Color(1.0f, 0.85f, 0.2f, 1f);

    [Header("실시간 상태 (읽기 전용)")]
    [SerializeField] private bool isLocked;
    [SerializeField] private float unlockProgress;
    [SerializeField] private GameObject currentCarrier;
    [SerializeField] private GameObject currentUnlocker;
    [SerializeField] private SoundSource lockedBy = SoundSource.None;

    public bool IsLocked => isLocked;
    public float UnlockProgress => unlockProgress;
    public float UnlockNormalized => unlockDuration > 0f ? Mathf.Clamp01(unlockProgress / unlockDuration) : 0f;
    public GameObject CurrentCarrier => currentCarrier;
    public GameObject CurrentUnlocker => currentUnlocker;
    public SoundSource LockedBy => lockedBy;

    private MazeArea area;
    private Rigidbody rb;
    private BoxCollider boxCollider;
    private Renderer meshRenderer;
    private MaterialPropertyBlock propBlock;

    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private Vector3 grabOffset;
    private float lastUnlockChannelTime;
    private float unlockSoundTimer;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private void Awake()
    {
        initialPosition = transform.position;
        initialRotation = transform.rotation;
        EnsureComponents();
    }

    public void Initialize(MazeArea mazeArea)
    {
        area = mazeArea;
        initialPosition = transform.position;
        initialRotation = transform.rotation;
        EnsureComponents();
    }

    private void EnsureComponents()
    {
        if (boxCollider == null) boxCollider = GetComponent<BoxCollider>();
        if (meshRenderer == null) meshRenderer = GetComponent<Renderer>();
        if (propBlock == null) propBlock = new MaterialPropertyBlock();
        if (area == null) area = GetComponentInParent<MazeArea>();

        if (rb == null) rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.mass = 20f;
        rb.useGravity = false;
        rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.isKinematic = true;

        // 기존 머티리얼 기본 색상 보존
        if (meshRenderer != null && meshRenderer.sharedMaterial != null)
        {
            if (meshRenderer.sharedMaterial.HasProperty(BaseColorId))
                unlockedColor = meshRenderer.sharedMaterial.GetColor(BaseColorId);
            else if (meshRenderer.sharedMaterial.HasProperty(ColorId))
                unlockedColor = meshRenderer.sharedMaterial.GetColor(ColorId);
        }

        UpdateVisualState();
    }

    private void Update()
    {
        // 채널링 해제 중 이탈(거리 초과 또는 입력 중단) 감지
        if (currentUnlocker != null)
        {
            bool outOfRange = GetSurfaceDistance(currentUnlocker.transform.position) > (interactionDistance + 0.5f);
            bool timedOut = (Time.time - lastUnlockChannelTime) > 0.35f;

            if (!isLocked || outOfRange || timedOut || !currentUnlocker.activeInHierarchy)
            {
                CancelChannelUnlock();
            }
        }
    }

    private void FixedUpdate()
    {
        if (currentCarrier == null) return;

        // 운반 대상이 비활성화되거나 거리가 너무 멀어지면 잡기 해제
        if (isLocked || !currentCarrier.activeInHierarchy || GetSurfaceDistance(currentCarrier.transform.position) > interactionDistance + 1.5f)
        {
            StopGrab();
            return;
        }

        // 운반자 위치 추적 이동
        Vector3 desiredPos = currentCarrier.transform.position + grabOffset;
        desiredPos.y = initialPosition.y;

        if (rb != null)
        {
            rb.MovePosition(desiredPos);
            rb.linearVelocity = Vector3.zero;
        }
    }

    public void ResetBox()
    {
        StopGrab();
        CancelChannelUnlock();

        isLocked = startLocked;
        lockedBy = SoundSource.None;
        unlockProgress = 0f;

        FreezePhysics();
        transform.position = initialPosition;
        transform.rotation = initialRotation;

        UpdateVisualState();
    }

    /// <summary>
    /// 대상 위치에서 상자 표면까지의 거리 계산
    /// </summary>
    public float GetSurfaceDistance(Vector3 actorPosition)
    {
        Vector3 flatActor = new Vector3(actorPosition.x, transform.position.y, actorPosition.z);
        Vector3 closest = boxCollider != null ? boxCollider.ClosestPoint(flatActor) : transform.position;
        closest.y = transform.position.y;
        return Vector3.Distance(flatActor, closest);
    }

    /// <summary>
    /// 대상과 상자 사이에 벽이 가로막고 있지 않은지(시야 확보) 검사
    /// </summary>
    public bool HasLineOfSight(Vector3 actorPosition, GameObject actor = null)
    {
        Vector3 origin = new Vector3(actorPosition.x, transform.position.y + 0.5f, actorPosition.z);
        Vector3 targetPoint = boxCollider != null ? boxCollider.ClosestPoint(origin) : transform.position;
        targetPoint.y = origin.y;

        Vector3 rayDir = targetPoint - origin;
        float dist = rayDir.magnitude;
        if (dist < 0.1f) return true;

        RaycastHit[] hits = Physics.RaycastAll(origin, rayDir.normalized, dist + 0.1f, ~0, QueryTriggerInteraction.Ignore);
        foreach (var hit in hits)
        {
            if (hit.collider == boxCollider || hit.collider.transform.IsChildOf(transform)) continue;
            if (actor != null && (hit.collider.gameObject == actor || hit.collider.transform.IsChildOf(actor.transform))) continue;

            return false;
        }
        return true;
    }

    public bool IsInRange(Vector3 actorPosition, GameObject actor = null)
    {
        return GetSurfaceDistance(actorPosition) <= interactionDistance && HasLineOfSight(actorPosition, actor);
    }

    /// <summary>
    /// 상자 잡기 시도
    /// </summary>
    public bool TryGrab(GameObject actor)
    {
        if (actor == null || isLocked || (currentCarrier != null && currentCarrier != actor)) return false;
        if (!IsInRange(actor.transform.position, actor)) return false;
        if (currentCarrier == actor) return true;

        currentCarrier = actor;
        grabOffset = transform.position - actor.transform.position;
        grabOffset.y = 0f;

        if (rb != null)
        {
            rb.isKinematic = false;
            rb.linearVelocity = Vector3.zero;
        }

        UpdateVisualState();
        return true;
    }

    /// <summary>
    /// 상자 잡기 해제
    /// </summary>
    public void StopGrab(GameObject actor = null)
    {
        if (actor != null && currentCarrier != actor) return;
        if (currentCarrier == null) return;

        currentCarrier = null;
        FreezePhysics();
        UpdateVisualState();
    }

    /// <summary>
    /// 상자 고정 (소리 발생)
    /// </summary>
    public bool TryLock(GameObject actor, SoundSource source)
    {
        if (actor == null || isLocked || !IsInRange(actor.transform.position, actor)) return false;

        StopGrab();
        CancelChannelUnlock();

        isLocked = true;
        lockedBy = source;

        FreezePhysics();

        if (area != null)
        {
            area.EmitSound(transform.position, source, "BoxLock");
        }

        UpdateVisualState();
        return true;
    }

    /// <summary>
    /// 상자 채널링 해제 (5초 유지)
    /// </summary>
    public bool TryChannelUnlock(GameObject actor, SoundSource source, float deltaTime)
    {
        if (actor == null || !isLocked) return false;
        if (!IsInRange(actor.transform.position, actor))
        {
            if (currentUnlocker == actor) CancelChannelUnlock();
            return false;
        }
        if (currentUnlocker != null && currentUnlocker != actor) return false;

        // 채널링 시작
        if (currentUnlocker != actor)
        {
            currentUnlocker = actor;
            unlockProgress = 0f;
            unlockSoundTimer = 0f;
            if (area != null) area.EmitSound(transform.position, source, "BoxUnlockStart");
        }

        lastUnlockChannelTime = Time.time;
        unlockProgress += deltaTime;

        // 주기적 채널링 소리
        unlockSoundTimer -= deltaTime;
        if (unlockSoundTimer <= 0f)
        {
            if (area != null) area.EmitSound(transform.position, source, "BoxUnlockChanneling");
            unlockSoundTimer = 0.5f;
        }

        // 해제 완료
        if (unlockProgress >= unlockDuration)
        {
            isLocked = false;
            lockedBy = SoundSource.None;
            unlockProgress = 0f;
            currentUnlocker = null;

            if (area != null) area.EmitSound(transform.position, source, "BoxUnlockComplete");

            UpdateVisualState();
            return true;
        }

        UpdateVisualState();
        return false;
    }

    public void CancelChannelUnlock(GameObject actor = null)
    {
        if (actor != null && currentUnlocker != actor) return;
        if (currentUnlocker == null && unlockProgress <= 0f) return;

        currentUnlocker = null;
        unlockProgress = 0f;
        UpdateVisualState();
    }

    private void FreezePhysics()
    {
        if (rb == null) return;
        if (!rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        rb.isKinematic = true;
    }

    private void UpdateVisualState()
    {
        if (meshRenderer == null) return;
        if (propBlock == null) propBlock = new MaterialPropertyBlock();

        Color targetColor = unlockedColor;
        if (isLocked)
        {
            targetColor = (currentUnlocker != null && unlockProgress > 0f)
                ? Color.Lerp(lockedColor, unlockingColor, UnlockNormalized)
                : lockedColor;
        }
        else if (currentCarrier != null)
        {
            targetColor = grabbedColor;
        }

        meshRenderer.GetPropertyBlock(propBlock);
        propBlock.SetColor(BaseColorId, targetColor);
        propBlock.SetColor(ColorId, targetColor);
        meshRenderer.SetPropertyBlock(propBlock);
    }

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
