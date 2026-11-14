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
/// 미로 술래잡기 환경 매니저:
/// 에이전트(PlayerAgent, EnemyAgent), 상호작용 객체(KeyItem, EscapeDoor, MovableBox)를 등록 및 관리하며,
/// 양방향 소리 전파 및 에피소드 종료/리셋을 통합 제어합니다.
/// </summary>
public class MazeArea : MonoBehaviour
{
    /// <summary>플레이어 탈출 성공 시 플레이어에게 부여되는 승리 보상</summary>
    private const float PlayerEscapeReward = 1.0f;

    /// <summary>플레이어 탈출 성공 시 적에게 부여되는 패배 페널티</summary>
    private const float EnemyEscapePenalty = -1.0f;

    /// <summary>플레이어 체포 시 플레이어에게 부여되는 패배 페널티</summary>
    private const float PlayerCaughtPenalty = -1.0f;

    /// <summary>플레이어 체포 시 적에게 부여되는 승리 보상</summary>
    private const float EnemyCatchReward = 1.0f;

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


    [Header("상호작용 객체")]
    [Tooltip("환경 내의 열쇠 아이템 목록")]
    public List<KeyItem> keys = new List<KeyItem>();

    [Tooltip("환경 내의 탈출문 목록")]
    public List<EscapeDoor> doors = new List<EscapeDoor>();

    [Tooltip("환경 내의 조작 가능한 상자 목록")]
    public List<MovableBox> boxes = new List<MovableBox>();

    [Header("소리 전파 설정")]
    [Tooltip("발생한 소리가 에이전트에 의해 감지 가능한 지속 시간 (초)")]
    public float soundDuration = 3.0f;

    // 플레이어가 낸 소리 (적이 감지)
    private SoundEvent playerSound;

    /// <summary>플레이어가 낸 소리의 현재 위치</summary>
    public Vector3 PlayerSoundPosition => playerSound.Position;

    /// <summary>플레이어가 낸 소리의 남은 지속 시간</summary>
    public float PlayerSoundTimer => playerSound.Timer;

    // 적이 낸 소리 (플레이어가 감지)
    private SoundEvent enemySound;

    /// <summary>적이 낸 소리의 현재 위치</summary>
    public Vector3 EnemySoundPosition => enemySound.Position;

    /// <summary>적이 낸 소리의 남은 지속 시간</summary>
    public float EnemySoundTimer => enemySound.Timer;

    // 중복 에피소드 종료 방지용 프레임 카운터
    private int fixedFrameCount;
    private int lastEpisodeEndFrame = -1;
    private bool suppressAreaReset;

    /// <summary>현재 활성화된 유효 플레이어 에이전트</summary>
    private PlayerAgent ActivePlayer => player != null && player.isActiveAndEnabled ? player : null;

    /// <summary>현재 활성화된 유효 적 에이전트</summary>
    private EnemyAgent ActiveEnemy => enemy != null && enemy.isActiveAndEnabled ? enemy : null;


    /// <summary>
    /// 컴포넌트 로드 시 하위 에이전트 및 상호작용 객체들의 참조를 자동 연결합니다.
    /// </summary>
    private void Awake()
    {
        SetupReferences();
    }

    /// <summary>
    /// 게임 시작 시 환경 전체를 초기 에피소드 상태로 리셋합니다.
    /// </summary>
    private void Start()
    {
        ResetArea();
    }

    /// <summary>
    /// 물리 틱마다 프레임 카운터를 갱신하고 활성 소리 타이머를 감산합니다.
    /// </summary>
    private void FixedUpdate()
    {
        fixedFrameCount++;
        playerSound.Tick(Time.fixedDeltaTime);
        enemySound.Tick(Time.fixedDeltaTime);
    }

    /// <summary>
    /// 플레이 모드와 에디터 모드 환경을 구분하여 안전하게 오브젝트를 파괴합니다.
    /// </summary>
    /// <param name="obj">파괴할 오브젝트</param>
    private static void SafeDestroy(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj);
    }

    /// <summary>
    /// 하위 계층 구조를 탐색하여 에이전트, 상자, 열쇠, 문 컴포넌트를 캐싱하고 상호 초기화합니다.
    /// </summary>
    public void SetupReferences()
    {
        // 1. Cubes 컨테이너에 잘못 부착되었을 수 있는 컴포넌트 제거 (부모-자식 Rigidbody 중첩 방지)
        Transform cubesContainer = transform.Find("Cubes");
        if (cubesContainer != null)
        {
            var mb = cubesContainer.GetComponent<MovableBox>();
            if (mb != null) SafeDestroy(mb);
            var rb = cubesContainer.GetComponent<Rigidbody>();
            if (rb != null) SafeDestroy(rb);
            var col = cubesContainer.GetComponent<Collider>();
            if (col != null) SafeDestroy(col);
        }

        // 2. 실제 개별 Cube 오브젝트에만 MovableBox 자동 등록
        foreach (var child in GetComponentsInChildren<Transform>(true))
        {
            if (child == transform || child.name == "Cubes") continue;

            if (child.name == "Cube" || child.name.StartsWith("Cube (") || child.CompareTag("Box") || child.CompareTag("LockedBox"))
            {
                if (child.GetComponent<MovableBox>() == null)
                {
                    child.gameObject.AddComponent<MovableBox>();
                }
            }
        }

        // 3. 조작 가능 상자 리스트 수집
        boxes.Clear();
        foreach (var box in GetComponentsInChildren<MovableBox>(true))
        {
            if (box.gameObject != gameObject && box.gameObject.name != "Cubes")
            {
                boxes.Add(box);
            }
        }

        // 4. 에이전트 참조 확인
        if (player == null) player = GetComponentInChildren<PlayerAgent>(true);
        if (enemy == null) enemy = GetComponentInChildren<EnemyAgent>(true);

        if (player != null) player.InitializeAgent(this);
        if (enemy != null) enemy.InitializeAgent(this);

        // 5. 열쇠 및 문 목록 갱신
        keys.Clear();
        keys.AddRange(GetComponentsInChildren<KeyItem>(true));

        doors.Clear();
        doors.AddRange(GetComponentsInChildren<EscapeDoor>(true));

        // 6. 개별 객체 초기화
        for (int i = 0; i < keys.Count; i++) keys[i].Initialize(this);
        for (int i = 0; i < doors.Count; i++) doors[i].Initialize(this);
        for (int i = 0; i < boxes.Count; i++) boxes[i].Initialize(this);

        SyncWallColliders();
        WarnIfManualControlsOverlap();
    }

    /// <summary>
    /// 부모 Wall에 메시가 없고 자식 프리팹만 위치가 조정된 경우,
    /// 허공에 남은 부모의 투명 BoxCollider를 비활성화하고 실제 자식 콜라이더에 Wall 태그를 부여합니다.
    /// </summary>
    private void SyncWallColliders()
    {
        Transform wallsRoot = transform.Find("Walls");
        if (wallsRoot == null) return;

        foreach (Transform wall in wallsRoot)
        {
            if (wall == null || wall.childCount == 0) continue;

            MeshFilter mf = wall.GetComponent<MeshFilter>();
            Collider parentCol = wall.GetComponent<Collider>();

            bool hasChildCollider = false;
            foreach (Collider col in wall.GetComponentsInChildren<Collider>(true))
            {
                if (col == parentCol) continue;
                hasChildCollider = true;
                if (!col.gameObject.CompareTag("Wall"))
                {
                    col.gameObject.tag = "Wall";
                }
            }

            if (hasChildCollider && parentCol != null && (mf == null || mf.sharedMesh == null))
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
    /// 반대편 에이전트도 EpisodeInterrupted 처리하여 전체 환경을 완벽하게 동기화 리셋합니다.
    /// </summary>
    /// <param name="agent">리셋을 시작한 에이전트</param>
    public void OnAgentEpisodeBegin(MazeAgentBase agent)
    {
        if (suppressAreaReset) return;
        if (lastEpisodeEndFrame == fixedFrameCount) return;
        lastEpisodeEndFrame = fixedFrameCount;

        suppressAreaReset = true;
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
        suppressAreaReset = false;

        ResetAreaInternal();
    }

    /// <summary>
    /// 소리, 상자, 열쇠 및 모든 에이전트의 위치와 상태를 초기 상태로 되돌립니다.
    /// </summary>
    private void ResetAreaInternal()
    {
        playerSound.Clear();
        enemySound.Clear();

        for (int i = 0; i < boxes.Count; i++) boxes[i].ResetBox();
        ResetKeys();

        if (player != null) player.ResetPosition();
        if (enemy != null) enemy.ResetPosition();

    }

    /// <summary>
    /// 지정된 위치에 소리를 등록하여 상대편 에이전트에게 전파합니다.
    /// </summary>
    /// <param name="position">소리 발생 월드 좌표</param>
    /// <param name="source">소리를 발생시킨 주체 (Player / Enemy)</param>
    /// <param name="reason">소리 발생 사유 (디버그 및 AI 알림용)</param>
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
    /// 씬 내의 모든 열쇠를 초기 스폰 위치로 복구하고 다시 활성화합니다.
    /// </summary>
    public void ResetKeys()
    {
        for (int i = 0; i < keys.Count; i++)
        {
            keys[i].ResetKey();
        }
    }

    /// <summary>
    /// 플레이어가 열쇠를 획득하려고 시도합니다. 이미 열쇠를 소지 중이면 거부됩니다.
    /// </summary>
    /// <param name="position">열쇠 위치</param>
    /// <returns>획득 성공 여부</returns>
    public bool TryCollectKey(Vector3 position)
    {
        var activePlayer = ActivePlayer;

        if (activePlayer != null && activePlayer.hasKey) return false;

        EmitSound(position, SoundSource.Player, "KeyCollected");
        if (activePlayer != null) activePlayer.OnKeyCollected();
        return true;
    }

    /// <summary>
    /// 주어진 오브젝트가 열쇠를 소지한 유효한 플레이어(PlayerAgent)인지 검사합니다.
    /// 주어진 오브젝트가 열쇠를 소지한 유효한 플레이어(PlayerAgent)인지 검사합니다.
    /// </summary>
    /// <param name="actor">검사할 대상 게임 오브젝트</param>
    /// <returns>열쇠 소지 플레이어이면 true</returns>
    public bool ActorHasKey(GameObject actor)
    {
        if (actor == null) return false;

        var agent = actor.GetComponentInParent<PlayerAgent>();
        return agent != null && agent.isActiveAndEnabled && agent.hasKey;
        return agent != null && agent.isActiveAndEnabled && agent.hasKey;
    }

    /// <summary>
    /// 현재 활성 플레이어의 Transform을 반환합니다.
    /// </summary>
    /// <returns>플레이어 Transform</returns>
    public Transform GetPlayerTransform()
    {
        return player != null ? player.transform : null;
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
    /// 승패 보상을 지급하고 양쪽 에이전트의 에피소드를 안전하게 종료한 뒤 환경을 리셋합니다.
    /// 동일 물리 프레임 내의 중복 호출을 차단합니다.
    /// </summary>
    /// <param name="playerReward">플레이어에게 지급할 보상</param>
    /// <param name="enemyReward">적에게 지급할 보상</param>
    private void FinishEpisode(float playerReward, float enemyReward)
    {
        if (lastEpisodeEndFrame == fixedFrameCount) return;
        lastEpisodeEndFrame = fixedFrameCount;

        var activePlayer = ActivePlayer;
        var activeEnemy = ActiveEnemy;

        suppressAreaReset = true;
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
    /// 지정된 위치에서 가장 가까운 활성 열쇠의 Transform을 반환합니다.
    /// </summary>
    /// <param name="fromPos">기준 위치</param>
    /// <returns>가장 가까운 KeyItem의 Transform</returns>
    public Transform GetClosestActiveKey(Vector3 fromPos)
    {
        return FindClosestTransform(keys, fromPos, key => key.gameObject.activeSelf);
    }

    /// <summary>
    /// 지정된 위치에서 가장 가까운 탈출문의 Transform을 반환합니다.
    /// </summary>
    /// <param name="fromPos">기준 위치</param>
    /// <returns>가장 가까운 EscapeDoor의 Transform</returns>
    public Transform GetClosestDoor(Vector3 fromPos)
    {
        return FindClosestTransform(doors, fromPos);
    }

    /// <summary>
    /// 컴포넌트 리스트 내에서 기준 좌표와 가장 가까운 유효 Transform을 검색하는 공통 헬퍼 메서드입니다.
    /// </summary>
    /// <typeparam name="T">Component 파생 타입</typeparam>
    /// <param name="items">검색 대상 리스트</param>
    /// <param name="fromPos">기준 위치</param>
    /// <param name="filter">추가 필터링 조건 (선택 사항)</param>
    /// <returns>최단 거리 Transform</returns>
    private static Transform FindClosestTransform<T>(List<T> items, Vector3 fromPos, System.Predicate<T> filter = null) where T : Component
    {
        Transform closest = null;
        float minDist = float.MaxValue;

        for (int i = 0; i < items.Count; i++)
        {
            T item = items[i];
            if (item == null || (filter != null && !filter(item))) continue;

            float dist = Vector3.Distance(fromPos, item.transform.position);
            if (dist < minDist)
            {
                minDist = dist;
                closest = item.transform;
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
