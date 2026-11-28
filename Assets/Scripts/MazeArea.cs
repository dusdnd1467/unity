using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 소리 발생 주체 식별 열거형:
/// None(없음), Player(플레이어), Enemy(적)
/// </summary>
public enum SoundSource
{
    None = 0,
    Player = 1,
    Enemy = 2
}

/// <summary>
/// 런타임 최초 1회 초기 위치/회전을 기록하고 리셋 시 복원하는 경량 스냅샷입니다.
/// </summary>
public struct InitialPose
{
    private Vector3 position;
    private Quaternion rotation;
    private bool captured;

    public Vector3 Position => position;

    public void CaptureOnce(Transform target)
    {
        if (captured || target == null) return;
        position = target.position;
        rotation = target.rotation;
        if (Application.isPlaying) captured = true;
    }

    public void Restore(Transform target, Rigidbody rb = null)
    {
        if (target == null) return;
        if (rb != null)
        {
            rb.position = position;
            rb.rotation = rotation;
        }
        target.SetPositionAndRotation(position, rotation);
    }
}

/// <summary>
/// 미로 술래잡기 환경 매니저:
/// 에이전트(PlayerAgent, EnemyAgent), 상호작용 객체(KeyItem, EscapeDoor, MovableBox)를 등록 및 관리하며,
/// 양방향 소리 전파 및 에피소드 종료/리셋을 통합 제어합니다.
/// </summary>
public class MazeArea : MonoBehaviour
{
    /// <summary>플레이어 탈출 성공 시 플레이어에게 부여되는 승리 보상 (열쇠 +0.5와 합쳐 총 +1.0)</summary>
    private const float PlayerEscapeReward = 0.5f;

    /// <summary>플레이어 탈출 성공 시 적에게 부여되는 패배 페널티</summary>
    private const float EnemyEscapePenalty = -1.0f;

    /// <summary>플레이어 체포 시 플레이어에게 부여되는 패배 페널티</summary>
    private const float PlayerCaughtPenalty = -1.0f;

    /// <summary>플레이어 체포 시 적에게 부여되는 승리 보상</summary>
    private const float EnemyCatchReward = 1.0f;

    /// <summary>제한 시간 초과 시 플레이어에게 부여되는 타임아웃 페널티 (열쇠 +0.5 획득 상태였다면 총합 0)</summary>
    private const float PlayerTimeoutPenalty = -0.5f;

    /// <summary>제한 시간 초과 시 적에게 부여되는 타임아웃 페널티</summary>
    private const float EnemyTimeoutReward = -0.5f;

    private const string WallLayerName = "Wall";
    private static int? cachedWallLayerMask;
    private static bool wallLayerMissingWarned;

    /// <summary>시야선 및 벽 차폐 검사에 사용하는 Wall 전용 물리 레이어 마스크</summary>
    public static int WallLayerMask
    {
        get
        {
            if (cachedWallLayerMask.HasValue) return cachedWallLayerMask.Value;

            int layer = LayerMask.NameToLayer(WallLayerName);
            if (layer < 0)
            {
                if (!wallLayerMissingWarned)
                {
                    wallLayerMissingWarned = true;
                    Debug.LogWarning("[MazeArea] 'Wall' 레이어가 TagManager에 등록되어 있지 않습니다.");
                }
                return 0;
            }

            cachedWallLayerMask = 1 << layer;
            return cachedWallLayerMask.Value;
        }
    }

    /// <summary>
    /// 발생한 소리의 위치 및 잔여 감지 시간 데이터를 관리하는 구조체
    /// </summary>
    private struct SoundEvent
    {
        /// <summary>소리가 발생한 월드 좌표</summary>
        public Vector3 Position;

        /// <summary>소리가 감지 가능한 잔여 시간 (초)</summary>
        public float Timer;

        /// <summary>
        /// 새로운 소리를 등록하고 타이머를 설정합니다.
        /// </summary>
        /// <param name="position">소리 발생 위치</param>
        /// <param name="duration">지속 시간</param>
        public void Start(Vector3 position, float duration)
        {
            Position = position;
            Timer = duration;
        }

        /// <summary>
        /// 매 물리 틱마다 타이머를 차감하고, 시간이 만료되면 위치를 초기화합니다.
        /// </summary>
        /// <param name="deltaTime">경과 시간</param>
        public void Tick(float deltaTime)
        {
            if (Timer <= 0f) return;
            Timer -= deltaTime;
            if (Timer <= 0f) Position = Vector3.zero;
        }

        /// <summary>
        /// 소리 정보를 즉시 제거합니다.
        /// </summary>
        public void Clear()
        {
            Position = Vector3.zero;
            Timer = 0f;
        }
    }

    [Header("에이전트 참조")]
    [Tooltip("학습 중인 플레이어 에이전트")]
    public PlayerAgent player;

    [Tooltip("학습 중인 적(술래) 에이전트")]
    public EnemyAgent enemy;

    [Header("계층 컨테이너")]
    [Tooltip("조작 가능한 상자(MovableBox)들을 담는 루트 컨테이너 (미지정 시 Cubes 탐색)")]
    [SerializeField] private Transform boxesRoot;

    [Tooltip("벽(Wall) 오브젝트들을 담는 루트 컨테이너 (미지정 시 Walls 탐색)")]
    [SerializeField] private Transform wallsRoot;

    [Header("상호작용 객체")]
    [Tooltip("환경 내의 열쇠 아이템 목록")]
    public List<KeyItem> keys = new List<KeyItem>();

    [Tooltip("환경 내의 탈출문 목록")]
    public List<EscapeDoor> doors = new List<EscapeDoor>();

    [Tooltip("환경 내의 조작 가능한 상자 목록")]
    public List<MovableBox> boxes = new List<MovableBox>();

    [Header("소리 전파 설정")]
    [Tooltip("발생한 소리가 에이전트에 의해 감지 가능한 지속 시간 (초)")]
    public float soundDuration = 7.0f;

    [Header("에피소드 제한 시간 설정")]
    [Tooltip("에피소드 최대 물리 스텝 수 (50Hz 기준 9000 = 180초, 0 이하이면 타임아웃 비활성화)")]
    public int maxEpisodeSteps = 9000;

    [Tooltip("에피소드 시작 후 적(술래)이 스폰 위치에서 대기하는 준비 시간 (초, 0 이하이면 비활성화)")]
    public float enemyPrepDuration = 5.0f;

    // 플레이어 / 적 소리 이벤트 (상대방이 감지)
    private SoundEvent playerSound;
    private SoundEvent enemySound;

    // 에피소드 경과 스텝 및 중복 종료 방지용 프레임 카운터
    private int episodeStepCount;
    private float episodeElapsedTime;
    private int fixedFrameCount;
    private int lastEpisodeEndFrame = -1;
    private bool suppressAreaReset;

    private PlayerAgent ActivePlayer => player != null && player.isActiveAndEnabled ? player : null;
    private EnemyAgent ActiveEnemy => enemy != null && enemy.isActiveAndEnabled ? enemy : null;

    /// <summary>에피소드 시작 직후 적(술래)의 초반 대기 시간(Prep Phase) 진행 여부</summary>
    public bool IsEnemyInPrepPhase => enemyPrepDuration > 0f && episodeElapsedTime < enemyPrepDuration;

    private void Awake()
    {
        CollectReferences(onlyIfMissing: true);
    }

    private void Start()
    {
        ResetArea();
    }

    private void FixedUpdate()
    {
        fixedFrameCount++;
        episodeElapsedTime += Time.fixedDeltaTime;
        playerSound.Tick(Time.fixedDeltaTime);
        enemySound.Tick(Time.fixedDeltaTime);

        if (maxEpisodeSteps > 0)
        {
            episodeStepCount++;
            if (episodeStepCount >= maxEpisodeSteps)
            {
                OnEpisodeTimeout();
            }
        }
    }

    /// <summary>
    /// 에디터 도구 및 컨텍스트 메뉴에서 호출하여 하위 에이전트, 상자, 열쇠, 문 참조를 재수집하고 벽 레이어/콜라이더를 동기화합니다.
    /// </summary>
    [ContextMenu("Setup References")]
    public void SetupReferences()
    {
        CollectReferences(onlyIfMissing: false);
        SyncWallColliders();
    }

    private void CollectReferences(bool onlyIfMissing)
    {
        ResolveContainerRoots();

        if (!onlyIfMissing || player == null) player = GetComponentInChildren<PlayerAgent>(true);
        if (!onlyIfMissing || enemy == null) enemy = GetComponentInChildren<EnemyAgent>(true);

        if (onlyIfMissing)
        {
            boxes.RemoveAll(box => box == null);
            keys.RemoveAll(key => key == null);
            doors.RemoveAll(door => door == null);
        }

        if (!onlyIfMissing || boxes.Count == 0) CollectBoxes();
        if (!onlyIfMissing || keys.Count == 0)
        {
            keys.Clear();
            keys.AddRange(GetComponentsInChildren<KeyItem>(true));
        }
        if (!onlyIfMissing || doors.Count == 0)
        {
            doors.Clear();
            doors.AddRange(GetComponentsInChildren<EscapeDoor>(true));
        }

        InitializeBoundObjects();
        WarnIfManualControlsOverlap();
    }

    private void ResolveContainerRoots()
    {
        if (boxesRoot == null) boxesRoot = transform.Find("Cubes");
        if (wallsRoot == null) wallsRoot = transform.Find("Walls");
    }

    private void CollectBoxes()
    {
        boxes.Clear();
        Transform searchRoot = boxesRoot != null ? boxesRoot : transform;
        foreach (var box in searchRoot.GetComponentsInChildren<MovableBox>(true))
        {
            if (box == null || box.transform == transform || box.transform == boxesRoot) continue;
            boxes.Add(box);
        }
    }

    private void InitializeBoundObjects()
    {
        if (player != null) player.InitializeAgent(this);
        if (enemy != null) enemy.InitializeAgent(this);

        for (int i = 0; i < keys.Count; i++)
        {
            if (keys[i] != null) keys[i].Initialize(this);
        }
        for (int i = 0; i < doors.Count; i++)
        {
            if (doors[i] != null) doors[i].Initialize(this);
        }
        for (int i = 0; i < boxes.Count; i++)
        {
            if (boxes[i] != null) boxes[i].Initialize(this);
        }
    }

    /// <summary>
    /// 부모 Wall에 메시가 없고 활성 자식 콜라이더만 존재하는 경우 허공에 남은 부모 콜라이더를 비활성화하고,
    /// 모든 활성 벽 콜라이더에 Wall 태그 및 Wall 레이어를 동기화합니다.
    /// </summary>
    private void SyncWallColliders()
    {
        if (wallsRoot == null) return;

        int wallLayer = LayerMask.NameToLayer(WallLayerName);

        foreach (Transform wall in wallsRoot)
        {
            if (wall == null) continue;

            if (wallLayer >= 0) wall.gameObject.layer = wallLayer;
            if (!wall.gameObject.CompareTag("Wall")) wall.gameObject.tag = "Wall";

            MeshFilter mf = wall.GetComponent<MeshFilter>();
            Collider parentCol = wall.GetComponent<Collider>();

            bool hasActiveChildCollider = false;
            foreach (Collider col in wall.GetComponentsInChildren<Collider>(true))
            {
                if (col == parentCol || !col.enabled) continue;
                hasActiveChildCollider = true;
                if (wallLayer >= 0) col.gameObject.layer = wallLayer;
                if (!col.gameObject.CompareTag("Wall")) col.gameObject.tag = "Wall";
            }

            if (hasActiveChildCollider && parentCol != null && (mf == null || mf.sharedMesh == null))
            {
                parentCol.enabled = false;
            }
        }
    }

    /// <summary>
    /// 플레이어와 적 에이전트가 동일한 키보드 입력 체계(WASD 또는 방향키)를 중복 점유하고 있는지 확인하여 경고합니다.
    /// </summary>
    private void WarnIfManualControlsOverlap()
    {
        if (player != null && enemy != null)
        {
            if ((player.UseWasd && enemy.UseWasd) || (player.UseArrows && enemy.UseArrows))
            {
                Debug.LogWarning("[MazeArea] Player와 Enemy가 동일한 수동 조작 체계를 사용 중입니다.", this);
            }
        }
    }

    /// <summary>
    /// 외부에서 호출 가능한 에피소드 전체 리셋 진입점입니다.
    /// </summary>
    public void ResetArea()
    {
        if (suppressAreaReset) return;
        ResetAreaInternal();
    }

    /// <summary>
    /// 에이전트 중 하나가 MaxStep 도달 등의 사유로 개별 리셋(OnEpisodeBegin)을 시작했을 때,
    /// 반대편 에이전트도 EpisodeInterrupted 처리하여 전체 환경을 동기화 리셋합니다.
    /// </summary>
    public void OnAgentEpisodeBegin(MazeAgentBase agent)
    {
        if (suppressAreaReset) return;

        RunSynchronizedReset(() =>
        {
            var activePlayer = ActivePlayer;
            var activeEnemy = ActiveEnemy;

            if (agent == activePlayer && activeEnemy != null)
            {
                activeEnemy.EpisodeInterrupted();
            }
            else if (agent == activeEnemy && activePlayer != null)
            {
                activePlayer.EpisodeInterrupted();
            }
        });
    }

    /// <summary>
    /// 소리, 상자, 열쇠 및 모든 에이전트의 위치와 상태를 초기 상태로 되돌립니다.
    /// </summary>
    private void ResetAreaInternal()
    {
        episodeStepCount = 0;
        episodeElapsedTime = 0f;
        playerSound.Clear();
        enemySound.Clear();

        for (int i = 0; i < boxes.Count; i++)
        {
            if (boxes[i] != null) boxes[i].ResetBox();
        }
        ResetKeys();

        if (player != null) player.ResetPosition();
        if (enemy != null) enemy.ResetPosition();
    }

    /// <summary>
    /// 지정된 위치에 소리를 등록하여 상대편 에이전트에게 전파합니다.
    /// </summary>
    public void EmitSound(Vector3 position, SoundSource source, string reason = "")
    {
        if (source == SoundSource.Player)
        {
            playerSound.Start(position, soundDuration);
        }
        else if (source == SoundSource.Enemy)
        {
            enemySound.Start(position, soundDuration);
        }
    }

    /// <summary>
    /// 지정된 주체의 활성 소리 위치와 잔여 시간을 조회합니다.
    /// </summary>
    public bool TryGetSound(SoundSource source, out Vector3 position, out float remaining)
    {
        SoundEvent sound = source == SoundSource.Player ? playerSound
            : source == SoundSource.Enemy ? enemySound
            : default;

        position = sound.Position;
        remaining = sound.Timer;
        return remaining > 0f;
    }

    /// <summary>
    /// 씬 내의 모든 열쇠를 초기 스폰 위치로 복구하고 다시 활성화합니다.
    /// </summary>
    public void ResetKeys()
    {
        for (int i = 0; i < keys.Count; i++)
        {
            if (keys[i] != null) keys[i].ResetKey();
        }
    }

    /// <summary>
    /// 플레이어가 열쇠를 획득하려고 시도합니다. 이미 열쇠를 소지 중이면 거부됩니다.
    /// </summary>
    public bool TryCollectKey(Vector3 position)
    {
        var activePlayer = ActivePlayer;
        if (activePlayer == null || activePlayer.hasKey) return false;

        EmitSound(position, SoundSource.Player, "KeyCollected");
        activePlayer.OnKeyCollected();
        return true;
    }

    /// <summary>
    /// 주어진 오브젝트가 열쇠를 소지한 유효한 플레이어(PlayerAgent)인지 검사합니다.
    /// </summary>
    public bool ActorHasKey(GameObject actor)
    {
        if (actor == null) return false;

        var agent = actor.GetComponentInParent<PlayerAgent>();
        return agent != null && agent.isActiveAndEnabled && agent.hasKey;
    }

    /// <summary>
    /// 현재 활성 플레이어의 Transform을 반환합니다.
    /// </summary>
    public Transform GetPlayerTransform()
    {
        var activePlayer = ActivePlayer;
        return activePlayer != null ? activePlayer.transform : null;
    }

    /// <summary>
    /// 플레이어가 탈출문에 도달하여 탈출에 성공했을 때 호출됩니다.
    /// </summary>
    public void OnPlayerEscaped() => FinishEpisode(PlayerEscapeReward, EnemyEscapePenalty);

    /// <summary>
    /// 적이 플레이어를 추적하여 잡았을 때 호출됩니다.
    /// </summary>
    public void EndEpisodeByCatch() => FinishEpisode(PlayerCaughtPenalty, EnemyCatchReward);

    /// <summary>
    /// 제한 스텝(maxEpisodeSteps)을 초과해 타임아웃으로 에피소드가 종료될 때 호출됩니다.
    /// </summary>
    private void OnEpisodeTimeout() => FinishEpisode(PlayerTimeoutPenalty, EnemyTimeoutReward);

    private void FinishEpisode(float playerReward, float enemyReward)
    {
        RunSynchronizedReset(() =>
        {
            var activePlayer = ActivePlayer;
            var activeEnemy = ActiveEnemy;

            if (activePlayer != null)
            {
                activePlayer.AddReward(playerReward);
                activePlayer.EndEpisode();
            }
            if (activeEnemy != null)
            {
                activeEnemy.AddReward(enemyReward);
                activeEnemy.EndEpisode();
            }
        });
    }

    private void RunSynchronizedReset(System.Action endAgentsAction)
    {
        if (lastEpisodeEndFrame == fixedFrameCount) return;
        lastEpisodeEndFrame = fixedFrameCount;

        // EndEpisode/EpisodeInterrupted가 재귀적으로 OnAgentEpisodeBegin을 다시 부르는 것을 방지
        suppressAreaReset = true;
        endAgentsAction?.Invoke();
        suppressAreaReset = false;

        ResetAreaInternal();
    }

    /// <summary>
    /// 지정된 위치에서 표면 기준 거리가 가장 가까운 상자를 검색합니다.
    /// </summary>
    /// <param name="fromPos">기준 위치</param>
    /// <returns>가장 가까운 MovableBox 인스턴스</returns>
    public MovableBox GetClosestBox(Vector3 fromPos)
    {
        MovableBox closest = null;
        float minDist = float.MaxValue;
        for (int i = 0; i < boxes.Count; i++)
        {
            MovableBox box = boxes[i];
            if (box == null || !box.gameObject.activeInHierarchy) continue;

            float dist = box.GetSurfaceDistance(fromPos);
            if (dist < minDist)
            {
                minDist = dist;
                closest = box;
            }
        }
        return closest;
    }

    /// <summary>
    /// 씬 뷰에서 현재 전파 중인 소리 파동의 위치와 범위를 가시화합니다.
    /// </summary>
    private void OnDrawGizmos()
    {
        DrawSoundWave(playerSound, new Color(0.2f, 0.6f, 1.0f));
        DrawSoundWave(enemySound, new Color(1.0f, 0.25f, 0.2f));
    }

    /// <summary>
    /// 개별 소리 이벤트의 잔여 시간에 따라 감쇠하는 구형 기즈모를 그립니다.
    /// </summary>
    /// <param name="sound">소리 이벤트 데이터</param>
    /// <param name="color">기즈모 표시 색상</param>
    private void DrawSoundWave(SoundEvent sound, Color color)
    {
        if (sound.Timer <= 0f) return;
        float ratio = Mathf.Clamp01(sound.Timer / Mathf.Max(0.01f, soundDuration));
        Gizmos.color = new Color(color.r, color.g, color.b, 0.6f * ratio);
        Gizmos.DrawWireSphere(sound.Position, Mathf.Lerp(6.0f, 1.5f, ratio));
    }
}
