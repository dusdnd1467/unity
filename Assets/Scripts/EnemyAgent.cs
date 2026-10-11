using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public class EnemyAgent : Agent
{
    public float moveSpeed = 6.0f;
    private Rigidbody rb;
    private MazeArea area;
    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private float previousDistanceToPlayer;

    public void InitializeEnemy(MazeArea mazeArea)
    {
        area = mazeArea;
        if (rb == null) rb = GetComponent<Rigidbody>();
        initialPosition = transform.position;
        initialRotation = transform.rotation;
    }

    public override void Initialize()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (area == null) area = GetComponentInParent<MazeArea>();
        initialPosition = transform.position;
        initialRotation = transform.rotation;
    }

    public override void OnEpisodeBegin()
    {
        if (area != null)
        {
            area.ResetArea();
        }
        else
        {
            ResetPosition();
        }
    }

    public void ResetPosition()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        transform.position = initialPosition;
        transform.rotation = initialRotation;
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (area != null && area.player != null)
        {
            previousDistanceToPlayer = Vector3.Distance(transform.position, area.player.transform.position);
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // 1. Sound relative position (3 floats)
        // 2. Sound timer remaining (1 float: 0.0 ~ 1.0)
        // Total: 4 floats
        if (area != null)
        {
            Vector3 soundRel = area.CurrentSoundPosition - transform.position;
            sensor.AddObservation(area.SoundTimer > 0f ? soundRel : Vector3.zero);
            sensor.AddObservation(area.SoundTimer / area.soundDuration);
        }
        else
        {
            sensor.AddObservation(Vector3.zero);
            sensor.AddObservation(0f);
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        float moveX = actions.ContinuousActions[0];
        float moveZ = actions.ContinuousActions[1];

        Vector3 move = new Vector3(moveX, 0f, moveZ);
        if (move.sqrMagnitude > 1f) move.Normalize();

        if (rb != null)
        {
            rb.MovePosition(rb.position + move * moveSpeed * Time.fixedDeltaTime);
        }

        // 거리 기반 보상 성형 (Reward Shaping): 플레이어에게 가까워지면 보상(+), 멀어지면 페널티(-)
        if (area != null && area.player != null)
        {
            float currentDistance = Vector3.Distance(transform.position, area.player.transform.position);
            float distanceDelta = previousDistanceToPlayer - currentDistance;
            AddReward(distanceDelta * 0.01f);
            previousDistanceToPlayer = currentDistance;
        }

        // Time step penalty
        AddReward(-0.0005f);
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuous = actionsOut.ContinuousActions;
        float x = 0f;
        float z = 0f;

        if (UnityEngine.InputSystem.Keyboard.current != null)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) x = -1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) x = 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) z = -1f;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) z = 1f;
        }

        continuous[0] = x;
        continuous[1] = z;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Player"))
        {
            // Caught the player!
            AddReward(1.0f);
            if (area != null)
            {
                area.EndEpisodeByCatch();
            }
            else
            {
                EndEpisode();
            }
        }
    }
}
