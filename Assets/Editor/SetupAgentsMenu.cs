using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;

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

        // 2. PlayerAgent 추가 (MazeAgentBase가 Rigidbody를 요구하므로 먼저 추가되며, 이후 런타임에 물리 설정이 적용됨)
        PlayerAgent playerAgent = GetOrAddComponent<PlayerAgent>(playerObj);
        playerAgent.MaxStep = 5000;
        playerAgent.moveSpeed = 5.0f;
        playerAgent.autoUnlockEnemyLockedBox = true;
        playerAgent.useArrows = true;
        playerAgent.useWasd = false;

        // 3. Rigidbody 설정 (런타임 EnsureReferences와 동일한 값을 에디터에서도 미리 반영)
        Rigidbody rb = GetOrAddComponent<Rigidbody>(playerObj);
        rb.isKinematic = false;
        rb.useGravity = false;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;

        // 4. BehaviorParameters 설정
        // 상자 상호작용은 이산 액션 브랜치(0:없음 1:잡기/놓기 2:고정 3:해제)를 추가하면 RL 정책도 사용할 수 있지만,
        // 기존 학습 결과(.onnx)와 호환되도록 연속 이동 2개만 사용합니다.
        BehaviorParameters bp = GetOrAddComponent<BehaviorParameters>(playerObj);
        bp.BehaviorName = "Player";
        bp.BehaviorType = BehaviorType.Default;
        bp.BrainParameters.VectorObservationSize = PlayerAgent.VectorObservationSize;
        bp.BrainParameters.NumStackedVectorObservations = 1;
        bp.BrainParameters.ActionSpec = ActionSpec.MakeContinuous(2);
        bp.TeamId = 1;

        // 5. DecisionRequester 설정
        DecisionRequester dr = GetOrAddComponent<DecisionRequester>(playerObj);
        dr.DecisionPeriod = 5;
        dr.TakeActionsBetweenDecisions = true;

        // 6. RayPerceptionSensor3D 설정
        RayPerceptionSensorComponent3D raySensor = GetOrAddComponent<RayPerceptionSensorComponent3D>(playerObj);
        raySensor.SensorName = "PlayerRaySensor";
        raySensor.DetectableTags = new List<string> { "Wall", "Enemy", "Door", "Key" };
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

        foreach (Object modified in new Object[] { playerObj, playerAgent, rb, bp, dr, raySensor, area })
        {
            EditorUtility.SetDirty(modified);
        }
        EditorSceneManager.MarkSceneDirty(playerObj.scene);

        Debug.Log("[SetupAgentsMenu] ✅ PlayerAgent 및 강화학습 컴포넌트 설정이 완료되었습니다! 씬을 저장해주세요.");
    }

    [MenuItem("Tools/ML-Agents/Restore Legacy SimplePlayerAI")]
    public static void RestoreLegacyAI()
    {
        MazeArea area = Object.FindFirstObjectByType<MazeArea>();
        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj == null)
        {
            Debug.LogError("[SetupAgentsMenu] Player 태그를 가진 오브젝트를 찾을 수 없습니다.");
            return;
        }

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
            Undo.RecordObject(area, "Restore Legacy AI");
            area.legacyPlayerAI = simpleAI;
            EditorUtility.SetDirty(area);
        }

        EditorUtility.SetDirty(playerObj);
        EditorSceneManager.MarkSceneDirty(playerObj.scene);
        Debug.Log("[SetupAgentsMenu] ↩️ 기존 SimplePlayerAI로 복원되었습니다.");
    }

    /// <summary>컴포넌트를 가져오고, 없으면 Undo를 지원하는 방식으로 추가합니다.</summary>
    private static T GetOrAddComponent<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(target);
    }
}
