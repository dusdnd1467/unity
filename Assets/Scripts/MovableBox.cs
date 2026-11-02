using UnityEngine;

/// <summary>
/// 에이전트(적 또는 플레이어)가 잡고 운반(Grab), 고정(Lock), 5초간 채널링 해제(Unlock)할 수 있는 상자 컴포넌트입니다.
/// 상자는 항상 Kinematic Rigidbody이며, 운반 중에는 운반자를 따라 이동합니다.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
[RequireComponent(typeof(Rigidbody))]
public class MovableBox : MonoBehaviour
{
    private const float UnlockChannelTimeout = 0.4f;
    private const float UnlockRangeMargin = 1.0f;
    private const float GrabRangeMargin = 2.5f;
    private const float UnlockSoundInterval = 0.5f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly RaycastHit[] LineOfSightHits = new RaycastHit[16];

    [Header("상호작용 설정")]
    [Tooltip("고정된 상자를 해제하는 데 걸리는 시간 (초)")]
    public float unlockDuration = 5.0f;

    [Tooltip("상자 표면으로부터 상호작용 가능한 최대 거리")]
    public float interactionDistance = 2.5f;

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

    private Material runtimeMaterial;
    private int colorPropertyId;
    private bool materialResolved;

    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private bool initialPoseCaptured;

    private Vector3 grabOffset;
    private float lastUnlockChannelTime;
    private float unlockSoundTimer;

    private void Awake()
    {
        EnsureComponents();
    }

    private void OnDestroy()
    {
        if (runtimeMaterial != null) Destroy(runtimeMaterial);
    }

    public void Initialize(MazeArea mazeArea)
    {
        area = mazeArea;
        EnsureComponents();
    }

    private void EnsureComponents()
    {
        if (boxCollider == null) boxCollider = GetComponent<BoxCollider>();
        if (meshRenderer == null) meshRenderer = GetComponent<Renderer>();
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (area == null) area = GetComponentInParent<MazeArea>();

        rb.useGravity = false;
        rb.isKinematic = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        if (!initialPoseCaptured)
        {
            initialPosition = transform.position;
            initialRotation = transform.rotation;
            initialPoseCaptured = true;
        }

        EnsureRuntimeMaterial();
        UpdateVisualState();
    }

    private void EnsureRuntimeMaterial()
    {
        if (materialResolved || meshRenderer == null || !Application.isPlaying) return;
        materialResolved = true;

        Material shared = meshRenderer.sharedMaterial;
        if (shared == null) return;

        if (shared.HasProperty(BaseColorId)) colorPropertyId = BaseColorId;
        else if (shared.HasProperty(ColorId)) colorPropertyId = ColorId;
        else return;

        unlockedColor = shared.GetColor(colorPropertyId);
        runtimeMaterial = meshRenderer.material;
    }

    private void Update()
    {
        if (currentUnlocker == null) return;

        bool outOfRange = GetSurfaceDistance(currentUnlocker.transform.position) > interactionDistance + UnlockRangeMargin;
        bool timedOut = (Time.time - lastUnlockChannelTime) > UnlockChannelTimeout;

        if (!isLocked || outOfRange || timedOut || !currentUnlocker.activeInHierarchy)
        {
            CancelChannelUnlock();
        }
    }

    private void FixedUpdate()
    {
        if (currentCarrier == null) return;

        if (isLocked
            || !currentCarrier.activeInHierarchy
            || GetSurfaceDistance(currentCarrier.transform.position) > interactionDistance + GrabRangeMargin)
        {
            StopGrab();
            return;
        }

        Vector3 desiredPos = currentCarrier.transform.position + grabOffset;
        desiredPos.y = initialPosition.y;
        rb.MovePosition(desiredPos);
    }

    public void ResetBox()
    {
        if (rb == null) EnsureComponents();

        StopGrab();
        CancelChannelUnlock();

        isLocked = startLocked;
        lockedBy = SoundSource.None;
        unlockProgress = 0f;

        rb.position = initialPosition;
        rb.rotation = initialRotation;
        transform.SetPositionAndRotation(initialPosition, initialRotation);

        UpdateVisualState();
    }

    public float GetSurfaceDistance(Vector3 actorPosition)
    {
        Vector3 flatActor = new Vector3(actorPosition.x, transform.position.y, actorPosition.z);
        Vector3 closest = boxCollider != null ? boxCollider.ClosestPoint(flatActor) : transform.position;
        closest.y = transform.position.y;
        return Vector3.Distance(flatActor, closest);
    }

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

    public bool IsInRange(Vector3 actorPosition, GameObject actor = null)
    {
        return GetSurfaceDistance(actorPosition) <= interactionDistance && HasLineOfSight(actorPosition, actor);
    }

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

    public void StopGrab(GameObject actor = null)
    {
        if (currentCarrier == null || (actor != null && currentCarrier != actor)) return;

        SetIgnoreCollision(currentCarrier, false);
        currentCarrier = null;
        UpdateVisualState();
    }

    public bool TryLock(GameObject actor, SoundSource source)
    {
        if (actor == null || isLocked || !IsInRange(actor.transform.position, actor)) return false;

        StopGrab();
        CancelChannelUnlock();

        isLocked = true;
        lockedBy = source;

        if (area != null) area.EmitSound(transform.position, source, "BoxLock");

        UpdateVisualState();
        return true;
    }

    public bool TryChannelUnlock(GameObject actor, SoundSource source, float deltaTime)
    {
        if (actor == null || !isLocked) return false;
        if (!IsInRange(actor.transform.position, actor))
        {
            if (currentUnlocker == actor) CancelChannelUnlock();
            return false;
        }
        if (currentUnlocker != null && currentUnlocker != actor) return false;

        if (currentUnlocker != actor)
        {
            currentUnlocker = actor;
            unlockProgress = 0f;
            unlockSoundTimer = 0f;
            if (area != null) area.EmitSound(transform.position, source, "BoxUnlockStart");
        }

        lastUnlockChannelTime = Time.time;
        unlockProgress += deltaTime;

        unlockSoundTimer -= deltaTime;
        if (unlockSoundTimer <= 0f)
        {
            if (area != null) area.EmitSound(transform.position, source, "BoxUnlockChanneling");
            unlockSoundTimer = UnlockSoundInterval;
        }

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

    private void SetIgnoreCollision(GameObject actor, bool ignore)
    {
        if (actor == null || boxCollider == null) return;

        foreach (Collider actorCollider in actor.GetComponentsInChildren<Collider>())
        {
            Physics.IgnoreCollision(actorCollider, boxCollider, ignore);
        }
    }

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

    private void UpdateVisualState()
    {
        if (runtimeMaterial != null)
        {
            runtimeMaterial.SetColor(colorPropertyId, GetCurrentColor());
        }
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
