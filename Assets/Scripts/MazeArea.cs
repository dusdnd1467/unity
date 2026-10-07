using UnityEngine;
using System.Collections.Generic;

public class MazeArea : MonoBehaviour
{
    [Header("Agent References")]
    public SimplePlayerAI player;
    public EnemyAgent enemy;

    [Header("Interactables")]
    public List<KeyItem> keys = new List<KeyItem>();
    public List<EscapeDoor> doors = new List<EscapeDoor>();

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

        // Reset all keys
        foreach (var key in keys)
        {
            if (key != null) key.gameObject.SetActive(true);
        }

        // Reset player
        if (player != null) player.ResetPlayer();

        // Reset enemy
        if (enemy != null) enemy.ResetPosition();
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
