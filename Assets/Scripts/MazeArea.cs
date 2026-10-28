using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

public class MazeArea : MonoBehaviour
{
    [Header("Agent References")]
    public SimplePlayerAI player;
    public EnemyAgent enemy;

    [Header("Interactables")]
    public List<KeyItem> keys = new List<KeyItem>();
    public List<EscapeDoor> doors = new List<EscapeDoor>();

    [Header("Key Spawn Settings")]
    [Tooltip("에피소드마다 스폰할 열쇠 개수 (기본 2개)")]
    public int activeKeyCount = 2;

    [Tooltip("열쇠가 생성될 수 있는 후보 위치 Transform 목록 (지정된 포인트들 중 activeKeyCount개가 무작위 선택됨)")]
    public List<Transform> keySpawnPoints = new List<Transform>();

    [Tooltip("keySpawnPoints가 비어있을 때 사용할 기본 좌표 6개 프리셋")]
    public List<Vector3> defaultSpawnPositions = new List<Vector3>()
    {
        new Vector3(-12.86f, 1f, 19.95f),  // 1. 북서쪽 (기존 Key1 위치)
        new Vector3(10.76f, 1f, -20.5f),   // 2. 남동쪽 (기존 Key 위치)
        new Vector3(12.0f, 1f, 18.0f),     // 3. 북동쪽
        new Vector3(-12.0f, 1f, -18.0f),   // 4. 남서쪽
        new Vector3(-18.0f, 1f, 0.0f),     // 5. 서쪽 통로
        new Vector3(18.0f, 1f, 0.0f)       // 6. 동쪽 통로
    };

    [Tooltip("열쇠의 Y 높이")]
    public float keyHeight = 1.0f;

    [Header("Sound Settings")]
    public float soundDuration = 3.0f;

    public Vector3 CurrentSoundPosition { get; private set; }
    public float SoundTimer { get; private set; }

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
        if (player != null) player.Initialize(this);
        if (enemy != null) enemy.InitializeEnemy(this);
        foreach (var key in keys)
        {
            if (key != null) key.Initialize(this);
        }
        foreach (var door in doors)
        {
            if (door != null) door.Initialize(this);
        }
    }

    private void Update()
    {
        if (SoundTimer > 0f)
        {
            SoundTimer -= Time.deltaTime;
            if (SoundTimer <= 0f)
            {
                SoundTimer = 0f;
                CurrentSoundPosition = Vector3.zero;
            }
        }
    }

    public void ResetArea()
    {
        SoundTimer = 0f;
        CurrentSoundPosition = Vector3.zero;

        // Reset and randomize all keys
        ResetKeys();

        // Reset player
        if (player != null) player.ResetPlayer();

        // Reset enemy
        if (enemy != null) enemy.ResetPosition();
    }

    public void ResetKeys()
    {
        // 1. 후보 위치 목록(Candidate Pool) 확보
        List<Vector3> candidatePool = new List<Vector3>();

        if (keySpawnPoints != null && keySpawnPoints.Count > 0)
        {
            foreach (var pt in keySpawnPoints)
            {
                if (pt != null) candidatePool.Add(pt.position);
            }
        }

        // keySpawnPoints가 비어있으면 6개 기본 좌표 프리셋 사용
        if (candidatePool.Count == 0 && defaultSpawnPositions != null)
        {
            candidatePool.AddRange(defaultSpawnPositions);
        }

        // 2. 후보 위치들 중 중복 없이 activeKeyCount(기본 2개)만큼 무작위 선택
        int countToSpawn = Mathf.Clamp(activeKeyCount, 1, keys.Count);
        countToSpawn = Mathf.Min(countToSpawn, candidatePool.Count);

        List<Vector3> chosenPoints = new List<Vector3>();
        List<Vector3> remainingPool = new List<Vector3>(candidatePool);

        for (int i = 0; i < countToSpawn; i++)
        {
            int randIndex = Random.Range(0, remainingPool.Count);
            chosenPoints.Add(remainingPool[randIndex]);
            remainingPool.RemoveAt(randIndex);
        }

        // 3. 열쇠들에게 선택된 위치 배정 및 활성화 (남는 열쇠는 비활성화)
        for (int i = 0; i < keys.Count; i++)
        {
            if (keys[i] == null) continue;

            if (i < chosenPoints.Count)
            {
                Vector3 targetPos = chosenPoints[i];
                // NavMesh에 유효하게 스냅
                if (NavMesh.SamplePosition(targetPos, out NavMeshHit hit, 3.0f, NavMesh.AllAreas))
                {
                    targetPos = hit.position;
                }
                targetPos.y = keyHeight;
                keys[i].ResetKey(targetPos);
            }
            else
            {
                // activeKeyCount(2개)를 초과하는 열쇠는 비활성화
                keys[i].gameObject.SetActive(false);
            }
        }
    }

    /// <summary>
    /// 인스펙터 컴포넌트 우클릭 메뉴: 씬에 KeySpawnPoints 오브젝트 및 자식 6개 포인트를 자동 생성합니다.
    /// </summary>
    [ContextMenu("씬에 열쇠 스폰 포인트 6개 생성 (Create 6 Key Spawn Points)")]
    public void CreateSpawnPointObjects()
    {
        GameObject container = GameObject.Find("KeySpawnPoints");
        if (container == null)
        {
            container = new GameObject("KeySpawnPoints");
            container.transform.parent = transform;
            container.transform.localPosition = Vector3.zero;
        }

        keySpawnPoints.Clear();

        for (int i = 0; i < defaultSpawnPositions.Count; i++)
        {
            string pointName = $"KeySpawnPoint_{i + 1}";
            Transform existingChild = container.transform.Find(pointName);
            GameObject ptObj = existingChild != null ? existingChild.gameObject : new GameObject(pointName);
            ptObj.transform.parent = container.transform;
            ptObj.transform.position = defaultSpawnPositions[i];
            keySpawnPoints.Add(ptObj.transform);
        }

        Debug.Log("[MazeArea] 6개의 열쇠 스폰 포인트 오브젝트가 생성되고 keySpawnPoints 리스트에 등록되었습니다. 씬 뷰에서 자유롭게 위치를 드래그하여 옮길 수 있습니다.");
    }

    private void OnDrawGizmosSelected()
    {
        // 1. keySpawnPoints 그리기
        if (keySpawnPoints != null && keySpawnPoints.Count > 0)
        {
            for (int i = 0; i < keySpawnPoints.Count; i++)
            {
                if (keySpawnPoints[i] != null)
                {
                    Gizmos.color = new Color(1f, 0.85f, 0.1f, 0.8f); // Gold
                    Gizmos.DrawWireSphere(keySpawnPoints[i].position, 0.7f);
                    Gizmos.color = new Color(1f, 0.85f, 0.1f, 0.3f);
                    Gizmos.DrawSphere(keySpawnPoints[i].position, 0.3f);
                }
            }
        }
        else if (defaultSpawnPositions != null)
        {
            // 2. defaultSpawnPositions 그리기
            for (int i = 0; i < defaultSpawnPositions.Count; i++)
            {
                Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.8f); // Cyan
                Gizmos.DrawWireSphere(defaultSpawnPositions[i], 0.7f);
                Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.3f);
                Gizmos.DrawSphere(defaultSpawnPositions[i], 0.3f);
            }
        }
    }

    public void OnKeyCollected(KeyItem key, Vector3 position)
    {
        CurrentSoundPosition = position;
        SoundTimer = soundDuration;

        if (player != null)
        {
            player.OnCollectedKey();
        }
    }

    public void OnPlayerEscaped()
    {
        if (enemy != null)
        {
            enemy.AddReward(-1.0f);
            enemy.EndEpisode();
        }
        else
        {
            ResetArea();
        }
    }

    public void EndEpisodeByCatch()
    {
        if (enemy != null)
        {
            enemy.EndEpisode();
        }
        else
        {
            ResetArea();
        }
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
}
