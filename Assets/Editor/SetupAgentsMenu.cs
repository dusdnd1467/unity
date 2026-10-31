using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
using Unity.MLAgents;

public static class SetupAgentsMenu
{
    [MenuItem("Tools/ML-Agents/Setup PlayerAgent (RL Setup)")]
    public static void SetupPlayerAgent()
    {
        MazeArea area = Object.FindFirstObjectByType<MazeArea>();
        if (area == null)
        {
            Debug.LogError("[SetupAgentsMenu] 씬에서 MazeArea를 찾을 수 없습니다.");
            return;
        }

        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj == null && area.player != null)
        {
            playerObj = area.player.gameObject;
        }
        if (playerObj == null)
        {
            Debug.LogError("[SetupAgentsMenu] Player 태그를 가진 오브젝트를 찾을 수 없습니다.");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(playerObj, "Setup PlayerAgent");

        // 1. 기존 단순 FSM AI 및 NavMeshAgent 비활성화
        SimplePlayerAI simpleAI = playerObj.GetComponent<SimplePlayerAI>();
        if (simpleAI != null)
        {
            simpleAI.enabled = false;
            Debug.Log("[SetupAgentsMenu] 기존 SimplePlayerAI 컴포넌트를 비활성화했습니다.");
        }

        UnityEngine.AI.NavMeshAgent navAgent = playerObj.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (navAgent != null)
        {
            navAgent.enabled = false;
            Debug.Log("[SetupAgentsMenu] NavMeshAgent 컴포넌트를 비활성화했습니다.");
        }

        // 2. Rigidbody 설정
        Rigidbody rb = playerObj.GetComponent<Rigidbody>();
        if (rb == null) rb = playerObj.AddComponent<Rigidbody>();
        rb.isKinematic = false;
        rb.useGravity = false;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;

        // 3. PlayerAgent 컴포넌트 추가 및 설정
        PlayerAgent playerAgent = playerObj.GetComponent<PlayerAgent>();
        if (playerAgent == null) playerAgent = playerObj.AddComponent<PlayerAgent>();
        playerAgent.MaxStep = 5000;
        playerAgent.moveSpeed = 5.0f;
        playerAgent.autoUnlockEnemyLockedBox = true;

        // 4. BehaviorParameters 설정
        BehaviorParameters bp = playerObj.GetComponent<BehaviorParameters>();
        if (bp == null) bp = playerObj.AddComponent<BehaviorParameters>();
        bp.BehaviorName = "Player";
        bp.BehaviorType = BehaviorType.Default;
        bp.BrainParameters.VectorObservationSize = 13;
        bp.BrainParameters.NumStackedVectorObservations = 1;
        bp.BrainParameters.ActionSpec = Unity.MLAgents.Actuators.ActionSpec.MakeContinuous(2);
        bp.TeamId = 1;

        // 5. DecisionRequester 설정
        DecisionRequester dr = playerObj.GetComponent<DecisionRequester>();
        if (dr == null) dr = playerObj.AddComponent<DecisionRequester>();
        dr.DecisionPeriod = 5;
        dr.TakeActionsBetweenDecisions = true;

        // 6. RayPerceptionSensor3D 설정
        RayPerceptionSensorComponent3D raySensor = playerObj.GetComponent<RayPerceptionSensorComponent3D>();
        if (raySensor == null) raySensor = playerObj.AddComponent<RayPerceptionSensorComponent3D>();
        raySensor.SensorName = "PlayerRaySensor";
        raySensor.DetectableTags = new System.Collections.Generic.List<string> { "Wall", "Enemy", "Door", "Key" };
        raySensor.RaysPerDirection = 8;
        raySensor.MaxRayDegrees = 180f;
        raySensor.SphereCastRadius = 0.25f;
        raySensor.RayLength = 25f;
        raySensor.ObservationStacks = 1;

        // 7. MazeArea 참조 연결
        Undo.RecordObject(area, "Update MazeArea Player Reference");
        area.player = playerAgent;
        if (simpleAI != null) area.legacyPlayerAI = simpleAI;
        area.SetupReferences();

        EditorUtility.SetDirty(playerObj);
        EditorUtility.SetDirty(area);
        EditorSceneManager.MarkSceneDirty(playerObj.scene);

        Debug.Log("[SetupAgentsMenu] ✅ PlayerAgent 및 강화학습 컴포넌트 설정이 완료되었습니다! 씬을 저장해주세요.");
    }

    [MenuItem("Tools/ML-Agents/Restore Legacy SimplePlayerAI")]
    public static void RestoreLegacyAI()
    {
        MazeArea area = Object.FindFirstObjectByType<MazeArea>();
        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj == null) return;

        Undo.RegisterFullObjectHierarchyUndo(playerObj, "Restore Legacy AI");

        PlayerAgent playerAgent = playerObj.GetComponent<PlayerAgent>();
        if (playerAgent != null) playerAgent.enabled = false;

        BehaviorParameters bp = playerObj.GetComponent<BehaviorParameters>();
        if (bp != null) bp.enabled = false;

        SimplePlayerAI simpleAI = playerObj.GetComponent<SimplePlayerAI>();
        if (simpleAI != null) simpleAI.enabled = true;

        UnityEngine.AI.NavMeshAgent navAgent = playerObj.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (navAgent != null) navAgent.enabled = true;

        if (area != null && simpleAI != null)
        {
            area.legacyPlayerAI = simpleAI;
            EditorUtility.SetDirty(area);
        }

        EditorUtility.SetDirty(playerObj);
        EditorSceneManager.MarkSceneDirty(playerObj.scene);
        Debug.Log("[SetupAgentsMenu] ↩️ 기존 SimplePlayerAI로 복원되었습니다.");
    }
}
