using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum SoundSource
{
    None = 0,
    Player = 1,
    Enemy = 2
}

/// <summary>
/// 미로 술래잡기 환경 매니저:
/// 에이전트(적, 플레이어), 상호작용 객체(열쇠, 문, 상자)를 관리하고,
/// 양방향 소리 전파 및 에피소드 종료/리셋을 제어합니다.
/// </summary>
public class MazeArea : MonoBehaviour
{
    private const float PlayerEscapeReward = 2.5f;
    private const float EnemyEscapePenalty = -1.0f;
    private const float PlayerCaughtPenalty = -1.0f;
    private const float EnemyCatchReward = 1.0f;

    /// <summary>소리 한 건의 위치와 남은 시간</summary>
    private struct SoundEvent
    {
        public Vector3 Position;
        public float Timer;

        public void Start(Vector3 position, float duration)
        {
            Position = position;
            Timer = duration;
        }

        public void Tick(float deltaTime)
        {
            if (Timer <= 0f) return;
            Timer -= deltaTime;
            if (Timer <= 0f) Position = Vector3.zero;
        }

        public void Clear()
        {
            Position = Vector3.zero;
            Timer = 0f;
        }
    }

    [Header("에이전트 참조")]
    public PlayerAgent player;
    public EnemyAgent enemy;

    [Header("레거시 AI 참조 (선택 사항)")]
    public SimplePlayerAI legacyPlayerAI;

    [Header("상호작용 객체")]
    public List<KeyItem> keys = new List<KeyItem>();
    public List<EscapeDoor> doors = new List<EscapeDoor>();
    public List<MovableBox> boxes = new List<MovableBox>();

    [Header("열쇠 스폰 설정")]
    [Tooltip("에피소드마다 활성화할 열쇠 개수")]
    public int activeKeyCount = 2;

    [Tooltip("열쇠 스폰 후보 위치 Transform 목록")]
    public List<Transform> keySpawnPoints = new List<Transform>();

    [Tooltip("기본 스폰 위치 프리셋 6개")]
    public List<Vector3> defaultSpawnPositions = new List<Vector3>()
    {
        new Vector3(-12.86f, 1f, 19.95f),  // 1. 북서
        new Vector3(10.76f, 1f, -20.5f),   // 2. 남동
        new Vector3(12.0f, 1f, 18.0f),     // 3. 북동
        new Vector3(-12.0f, 1f, -18.0f),   // 4. 남서
        new Vector3(-18.0f, 1f, 0.0f),     // 5. 서쪽 통로
        new Vector3(18.0f, 1f, 0.0f)       // 6. 동쪽 통로
    };

    public float keyHeight = 1.0f;

    [Header("소리 전파 설정")]
    public float soundDuration = 3.0f;

    // 플레이어가 낸 소리 (적이 감지)
    private SoundEvent playerSound;
    public Vector3 PlayerSoundPosition => playerSound.Position;
    public float PlayerSoundTimer => playerSound.Timer;

    // 적이 낸 소리 (플레이어가 감지)
    private SoundEvent enemySound;
    public Vector3 EnemySoundPosition => enemySound.Position;
    public float EnemySoundTimer => enemySound.Timer;

    // 중복 에피소드 종료 방지용 타임스탬프
    private float lastEpisodeEndFixedTime = -1f;
    private bool suppressAreaReset;

    private readonly List<Vector3> keyCandidates = new List<Vector3>();

    private PlayerAgent ActivePlayer => player != null && player.isActiveAndEnabled ? player : null;
    private EnemyAgent ActiveEnemy => enemy != null && enemy.isActiveAndEnabled ? enemy : null;
    private SimplePlayerAI ActiveLegacyPlayer => legacyPlayerAI != null && legacyPlayerAI.isActiveAndEnabled ? legacyPlayerAI : null;

    private void Awake()
    {
        SetupReferences();
    }

    private void Start()
    {
        ResetArea();
    }

    private void FixedUpdate()
    {
        playerSound.Tick(Time.fixedDeltaTime);
        enemySound.Tick(Time.fixedDeltaTime);
    }

    public void SetupReferences()
    {
        // 씬 내 Cube 오브젝트에 MovableBox 자동 등록
        foreach (var child in GetComponentsInChildren<Transform>(true))
        {
            if (child != transform && child.name.StartsWith("Cube") && child.GetComponent<MovableBox>() == null)
            {
                child.gameObject.AddComponent<MovableBox>();
            }
        }

        boxes.Clear();
        boxes.AddRange(GetComponentsInChildren<MovableBox>(true));

        if (player == null) player = GetComponentInChildren<PlayerAgent>(true);
        if (legacyPlayerAI == null) legacyPlayerAI = GetComponentInChildren<SimplePlayerAI>(true);
        if (enemy == null) enemy = GetComponentInChildren<EnemyAgent>(true);

        if (player != null) player.InitializeAgent(this);
        if (legacyPlayerAI != null) legacyPlayerAI.Initialize(this);
        if (enemy != null) enemy.InitializeAgent(this);

        for (int i = 0; i < keys.Count; i++) if (keys[i] != null) keys[i].Initialize(this);
        for (int i = 0; i < doors.Count; i++) if (doors[i] != null) doors[i].Initialize(this);
        for (int i = 0; i < boxes.Count; i++) if (boxes[i] != null) boxes[i].Initialize(this);

        WarnIfManualControlsOverlap();
    }

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

    /// <summary>에피소드 전체 리셋 (상자, 열쇠, 에이전트 위치)</summary>
    public void ResetArea()
    {
        if (suppressAreaReset) return;

        playerSound.Clear();
        enemySound.Clear();

        for (int i = 0; i < boxes.Count; i++) if (boxes[i] != null) boxes[i].ResetBox();
        ResetKeys();

        if (player != null) player.ResetPosition();
        if (enemy != null) enemy.ResetPosition();

        var legacy = ActiveLegacyPlayer;
        if (legacy != null) legacy.ResetPlayer();

        // 위치 초기화 후 보상 거리 추적 갱신
        if (player != null) player.RefreshTracking();
        if (enemy != null) enemy.RefreshTracking();
    }

    /// <summary>소리 발생 및 전파</summary>
    public void EmitSound(Vector3 position, SoundSource source, string reason = "")
    {
        if (source == SoundSource.Player)
        {
            playerSound.Start(position, soundDuration);
        }
        else if (source == SoundSource.Enemy)
        {
            enemySound.Start(position, soundDuration);
            var legacy = ActiveLegacyPlayer;
            if (legacy != null) legacy.OnEnemySoundHeard(position, reason);
        }
    }

    public void ResetKeys()
    {
        BuildKeyCandidates();

        int countToSpawn = Mathf.Min(Mathf.Max(1, activeKeyCount), keys.Count, keyCandidates.Count);
        int spawned = 0;

        for (int i = 0; i < keys.Count; i++)
        {
            KeyItem key = keys[i];
            if (key == null) continue;

            if (spawned < countToSpawn)
            {
                int randIndex = Random.Range(spawned, keyCandidates.Count);
                Vector3 targetPos = keyCandidates[randIndex];
                keyCandidates[randIndex] = keyCandidates[spawned];
                keyCandidates[spawned] = targetPos;

                if (NavMesh.SamplePosition(targetPos, out NavMeshHit hit, 3.0f, NavMesh.AllAreas))
                {
                    targetPos = hit.position;
                }
                targetPos.y = keyHeight;
                key.ResetKey(targetPos);
                spawned++;
            }
            else
            {
                key.gameObject.SetActive(false);
            }
        }
    }

    private void BuildKeyCandidates()
    {
        keyCandidates.Clear();

        if (keySpawnPoints != null && keySpawnPoints.Count > 0)
        {
            for (int i = 0; i < keySpawnPoints.Count; i++)
            {
                if (keySpawnPoints[i] != null) keyCandidates.Add(keySpawnPoints[i].position);
            }
        }

        if (keyCandidates.Count == 0 && defaultSpawnPositions != null)
        {
            keyCandidates.AddRange(defaultSpawnPositions);
        }
    }

    /// <summary>열쇠 획득 시도 (이미 소지 중이면 획득 실패)</summary>
    public bool TryCollectKey(Vector3 position)
    {
        var activePlayer = ActivePlayer;
        var legacy = ActiveLegacyPlayer;

        if ((activePlayer != null && activePlayer.hasKey) || (legacy != null && legacy.hasKey)) return false;

        EmitSound(position, SoundSource.Player, "KeyCollected");
        if (activePlayer != null) activePlayer.OnKeyCollected();
        if (legacy != null) legacy.OnCollectedKey();
        return true;
    }

    /// <summary>해당 오브젝트가 열쇠를 가진 활성 플레이어인지 확인</summary>
    public bool ActorHasKey(GameObject actor)
    {
        var agent = actor.GetComponentInParent<PlayerAgent>();
        if (agent != null && agent.isActiveAndEnabled) return agent.hasKey;

        var legacy = actor.GetComponentInParent<SimplePlayerAI>();
        return legacy != null && legacy.isActiveAndEnabled && legacy.hasKey;
    }

    /// <summary>현재 플레이어 역할을 하는 Transform 반환</summary>
    public Transform GetPlayerTransform()
    {
        if (ActivePlayer != null) return player.transform;
        if (ActiveLegacyPlayer != null) return legacyPlayerAI.transform;
        return player != null ? player.transform : (legacyPlayerAI != null ? legacyPlayerAI.transform : null);
    }

    public void OnPlayerEscaped() => FinishEpisode(PlayerEscapeReward, EnemyEscapePenalty);
    public void EndEpisodeByCatch() => FinishEpisode(PlayerCaughtPenalty, EnemyCatchReward);

    private void FinishEpisode(float playerReward, float enemyReward)
    {
        if (Mathf.Approximately(lastEpisodeEndFixedTime, Time.fixedTime)) return;
        lastEpisodeEndFixedTime = Time.fixedTime;

        var activePlayer = ActivePlayer;
        var activeEnemy = ActiveEnemy;

        if (activePlayer != null) activePlayer.AddReward(playerReward);
        if (activeEnemy != null) activeEnemy.AddReward(enemyReward);

        suppressAreaReset = true;
        if (activePlayer != null) activePlayer.EndEpisode();
        if (activeEnemy != null) activeEnemy.EndEpisode();
        suppressAreaReset = false;

        ResetArea();
    }

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

    public Transform GetClosestActiveKey(Vector3 fromPos)
    {
        return FindClosestTransform(keys, fromPos, key => key.gameObject.activeSelf);
    }

    public Transform GetClosestDoor(Vector3 fromPos)
    {
        return FindClosestTransform(doors, fromPos);
    }

    /// <summary>리스트 내 최단 거리 Transform 탐색 공통 헬퍼</summary>
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

    private void OnDrawGizmos()
    {
        DrawSoundWave(playerSound, new Color(0.2f, 0.6f, 1.0f));
        DrawSoundWave(enemySound, new Color(1.0f, 0.25f, 0.2f));
    }

    private void DrawSoundWave(SoundEvent sound, Color color)
    {
        if (sound.Timer <= 0f) return;
        float ratio = Mathf.Clamp01(sound.Timer / Mathf.Max(0.01f, soundDuration));
        Gizmos.color = new Color(color.r, color.g, color.b, 0.6f * ratio);
        Gizmos.DrawWireSphere(sound.Position, Mathf.Lerp(6.0f, 1.5f, ratio));
    }
}
