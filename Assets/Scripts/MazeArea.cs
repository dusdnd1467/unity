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
/// 양방향 소리 전파 및 에피소드 리셋을 제어합니다.
/// </summary>
public class MazeArea : MonoBehaviour
{
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
    public Vector3 PlayerSoundPosition { get; private set; }
    public float PlayerSoundTimer { get; private set; }

    // 적이 낸 소리 (플레이어가 감지)
    public Vector3 EnemySoundPosition { get; private set; }
    public float EnemySoundTimer { get; private set; }

    private int lastResetFrame = -1;

    private void Awake()
    {
        SetupReferences();
    }

    private void Start()
    {
        ResetArea();
    }

    public void SetupReferences()
    {
        // 씬 내 Cube 오브젝트에 MovableBox 자동 등록
        foreach (var child in GetComponentsInChildren<Transform>(true))
        {
            if (child != null && child != transform && child.name.StartsWith("Cube"))
            {
                if (child.GetComponent<MovableBox>() == null)
                    child.gameObject.AddComponent<MovableBox>();
            }
        }

        boxes.Clear();
        boxes.AddRange(GetComponentsInChildren<MovableBox>(true));

        if (player == null) player = GetComponentInChildren<PlayerAgent>(true);
        if (legacyPlayerAI == null) legacyPlayerAI = GetComponentInChildren<SimplePlayerAI>(true);
        if (enemy == null) enemy = GetComponentInChildren<EnemyAgent>(true);

        if (player != null) player.InitializePlayer(this);
        if (legacyPlayerAI != null) legacyPlayerAI.Initialize(this);
        if (enemy != null) enemy.InitializeEnemy(this);

        foreach (var key in keys) if (key != null) key.Initialize(this);
        foreach (var door in doors) if (door != null) door.Initialize(this);
        foreach (var box in boxes) if (box != null) box.Initialize(this);
    }

    private void Update()
    {
        UpdateSoundTimer(ref playerSoundTimerInternal, ref playerSoundPosInternal);
        UpdateSoundTimer(ref enemySoundTimerInternal, ref enemySoundPosInternal);

        PlayerSoundTimer = playerSoundTimerInternal;
        PlayerSoundPosition = playerSoundPosInternal;
        EnemySoundTimer = enemySoundTimerInternal;
        EnemySoundPosition = enemySoundPosInternal;
    }

    private float playerSoundTimerInternal;
    private Vector3 playerSoundPosInternal;
    private float enemySoundTimerInternal;
    private Vector3 enemySoundPosInternal;

    private void UpdateSoundTimer(ref float timer, ref Vector3 pos)
    {
        if (timer > 0f)
        {
            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                timer = 0f;
                pos = Vector3.zero;
            }
        }
    }

    /// <summary>
    /// 에피소드 전체 리셋
    /// </summary>
    public void ResetArea()
    {
        if (Time.frameCount == lastResetFrame && lastResetFrame > 0) return;
        lastResetFrame = Time.frameCount;

        playerSoundTimerInternal = 0f;
        playerSoundPosInternal = Vector3.zero;
        enemySoundTimerInternal = 0f;
        enemySoundPosInternal = Vector3.zero;

        PlayerSoundTimer = 0f;
        PlayerSoundPosition = Vector3.zero;
        EnemySoundTimer = 0f;
        EnemySoundPosition = Vector3.zero;

        foreach (var box in boxes) if (box != null) box.ResetBox();
        ResetKeys();

        if (player != null) player.ResetPosition();
        if (legacyPlayerAI != null) legacyPlayerAI.ResetPlayer();
        if (enemy != null) enemy.ResetPosition();
    }

    /// <summary>
    /// 열쇠 획득 또는 상자 조작 시 소리 발생
    /// </summary>
    public void EmitSound(Vector3 position, SoundSource source, string reason = "")
    {
        if (source == SoundSource.Player)
        {
            playerSoundPosInternal = position;
            playerSoundTimerInternal = soundDuration;
            PlayerSoundPosition = position;
            PlayerSoundTimer = soundDuration;
        }
        else if (source == SoundSource.Enemy)
        {
            enemySoundPosInternal = position;
            enemySoundTimerInternal = soundDuration;
            EnemySoundPosition = position;
            EnemySoundTimer = soundDuration;

            if (player != null) player.OnEnemySoundHeard(position, reason);
            if (legacyPlayerAI != null) legacyPlayerAI.OnEnemySoundHeard(position, reason);
        }
    }

    public void ResetKeys()
    {
        List<Vector3> candidatePool = new List<Vector3>();

        if (keySpawnPoints != null && keySpawnPoints.Count > 0)
        {
            foreach (var pt in keySpawnPoints)
                if (pt != null) candidatePool.Add(pt.position);
        }

        if (candidatePool.Count == 0 && defaultSpawnPositions != null)
        {
            candidatePool.AddRange(defaultSpawnPositions);
        }

        int countToSpawn = Mathf.Clamp(activeKeyCount, 1, Mathf.Min(keys.Count, candidatePool.Count));
        List<Vector3> remainingPool = new List<Vector3>(candidatePool);

        for (int i = 0; i < keys.Count; i++)
        {
            if (keys[i] == null) continue;

            if (i < countToSpawn && remainingPool.Count > 0)
            {
                int randIndex = Random.Range(0, remainingPool.Count);
                Vector3 targetPos = remainingPool[randIndex];
                remainingPool.RemoveAt(randIndex);

                if (NavMesh.SamplePosition(targetPos, out NavMeshHit hit, 3.0f, NavMesh.AllAreas))
                {
                    targetPos = hit.position;
                }
                targetPos.y = keyHeight;
                keys[i].ResetKey(targetPos);
            }
            else
            {
                keys[i].gameObject.SetActive(false);
            }
        }
    }

    public void OnKeyCollected(KeyItem key, Vector3 position)
    {
        EmitSound(position, SoundSource.Player, "KeyCollected");
        if (player != null) player.OnKeyCollected();
        if (legacyPlayerAI != null) legacyPlayerAI.OnCollectedKey();
    }

    /// <summary>
    /// 플레이어 탈출 성공 (플레이어 승리)
    /// </summary>
    public void OnPlayerEscaped()
    {
        if (player != null)
        {
            player.AddReward(2.5f);
            player.EndEpisode();
        }

        if (enemy != null)
        {
            enemy.AddReward(-1.0f);
            enemy.EndEpisode();
        }

        ResetArea();
    }

    /// <summary>
    /// 적이 플레이어를 잡음 (적 승리)
    /// </summary>
    public void EndEpisodeByCatch()
    {
        if (player != null)
        {
            player.AddReward(-1.0f);
            player.EndEpisode();
        }

        if (enemy != null)
        {
            enemy.EndEpisode();
        }

        ResetArea();
    }

    public MovableBox GetClosestBox(Vector3 fromPos)
    {
        MovableBox closest = null;
        float minDist = float.MaxValue;
        foreach (var box in boxes)
        {
            if (box != null && box.gameObject.activeInHierarchy)
            {
                float dist = box.GetSurfaceDistance(fromPos);
                if (dist < minDist)
                {
                    minDist = dist;
                    closest = box;
                }
            }
        }
        return closest;
    }

    public Transform GetClosestActiveKey(Vector3 fromPos)
    {
        Transform closest = null;
        float minDist = float.MaxValue;
        foreach (var key in keys)
        {
            if (key != null && key.gameObject.activeSelf)
            {
                float dist = Vector3.Distance(fromPos, key.transform.position);
                if (dist < minDist)
                {
                    minDist = dist;
                    closest = key.transform;
                }
            }
        }
        return closest;
    }

    public Transform GetClosestDoor(Vector3 fromPos)
    {
        Transform closest = null;
        float minDist = float.MaxValue;
        foreach (var door in doors)
        {
            if (door != null)
            {
                float dist = Vector3.Distance(fromPos, door.transform.position);
                if (dist < minDist)
                {
                    minDist = dist;
                    closest = door.transform;
                }
            }
        }
        return closest;
    }

    private void OnDrawGizmos()
    {
        DrawSoundWave(PlayerSoundTimer, PlayerSoundPosition, new Color(0.2f, 0.6f, 1.0f));
        DrawSoundWave(EnemySoundTimer, EnemySoundPosition, new Color(1.0f, 0.25f, 0.2f));
    }

    private void DrawSoundWave(float timer, Vector3 pos, Color color)
    {
        if (timer <= 0f) return;
        float ratio = Mathf.Clamp01(timer / Mathf.Max(0.01f, soundDuration));
        Gizmos.color = new Color(color.r, color.g, color.b, 0.6f * ratio);
        Gizmos.DrawWireSphere(pos, Mathf.Lerp(6.0f, 1.5f, ratio));
    }
}
