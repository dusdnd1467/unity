using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine.InputSystem;

public class KeyAgent : Agent
{
    public Transform key;

    public float moveSpeed = 5f;

    private Rigidbody rb;

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
    }

    public override void OnEpisodeBegin()
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        Vector3 relativePosition = key.position - transform.position;

        sensor.AddObservation(relativePosition);
        sensor.AddObservation(rb.linearVelocity);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        float moveX = actions.ContinuousActions[0];
        float moveZ = actions.ContinuousActions[1];

        Vector3 movement = new Vector3(moveX, 0f, moveZ);

        rb.MovePosition(
            rb.position + movement * moveSpeed * Time.fixedDeltaTime
        );
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuousActions = actionsOut.ContinuousActions;

        float x = 0f;
        float z = 0f;

        if (Keyboard.current.aKey.isPressed)
            x = -1f;

        if (Keyboard.current.dKey.isPressed)
            x = 1f;

        if (Keyboard.current.sKey.isPressed)
            z = -1f;

        if (Keyboard.current.wKey.isPressed)
            z = 1f;

        continuousActions[0] = x;
        continuousActions[1] = z;

        Debug.Log("Action X = " + x + ", Z = " + z);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Key"))
        {
            AddReward(1.0f);
            EndEpisode();
        }
    }
}