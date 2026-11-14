using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// 미로 술래잡기(MazeArea) 다중 환경 자동 복제 및 격자 배치 에디터 윈도우:
/// 1~16개의 환경을 원클릭으로 생성하고, 원본(Original)을 수정한 뒤에도
/// 언제든지 최신 원본 상태로 모든 복제본을 동일하게 재동기화할 수 있습니다.
/// </summary>
public class MazeAreaDuplicatorWindow : EditorWindow
{
    private const string ClonePrefix = "MazeArea_Clone_";

    [SerializeField] private MazeArea sourceArea;
    [SerializeField] private int targetCount = 4;
    [SerializeField] private float spacing = 100f;
    [SerializeField] private int columns = 0; // 0이면 자동(제곱근 기반)

    private Vector2 scrollPos;

    /// <summary>
    /// 유니티 상단 메뉴(Tools > ML-Agents > Multi-Area Duplicator)를 통해 윈도우를 엽니다.
    /// </summary>
    [MenuItem("Tools/ML-Agents/Multi-Area Duplicator (다중 환경 생성기)")]
    public static void ShowWindow()
    {
        var window = GetWindow<MazeAreaDuplicatorWindow>("Area Duplicator");
        window.minSize = new Vector2(380, 400);
        window.Show();
    }

    /// <summary>
    /// 에디터 윈도우 활성화 시 씬에서 원본 MazeArea를 자동 감지합니다.
    /// </summary>
    private void OnEnable()
    {
        FindOrCreateSourceArea();
    }

    /// <summary>
    /// 에디터 윈도우 GUI를 렌더링하고 사용자 입력을 처리합니다.
    /// </summary>
    private void OnGUI()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("🎮 MazeArea 다중 환경 병렬 복제기", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "원본 MazeArea를 원하는 개수(1~16)로 복제하여 격자 형태로 자동 배치합니다.\n" +
            "원본을 수정한 후 다시 숫자를 누르거나 [동기화]를 실행하면 최신 원본 상태가 모든 복제본에 똑같이 반영됩니다.",
            MessageType.Info
        );

        EditorGUILayout.Space(10);

        // 1. 원본 MazeArea 참조
        EditorGUILayout.LabelField("1. 원본 환경 (Original Template)", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        sourceArea = (MazeArea)EditorGUILayout.ObjectField("원본 MazeArea", sourceArea, typeof(MazeArea), true);
        if (GUILayout.Button("자동 감지", GUILayout.Width(75)))
        {
            FindOrCreateSourceArea();
        }
        EditorGUILayout.EndHorizontal();

        if (sourceArea == null)
        {
            EditorGUILayout.HelpBox("씬에 원본 MazeArea가 없습니다. 씬을 열거나 MazeArea를 선택해주세요.", MessageType.Warning);
            EditorGUILayout.EndScrollView();
            return;
        }

        // 현재 씬 상태 표시
        List<MazeArea> clones = GetCurrentClones();
        int totalActive = 1 + clones.Count;
        EditorGUILayout.HelpBox($"현재 씬 상태: 총 {totalActive}개 활성 (원본 1개 + 복제본 {clones.Count}개)", MessageType.None);

        EditorGUILayout.Space(10);

        // 2. 환경 개수 선택 (1 ~ 16)
        EditorGUILayout.LabelField("2. 환경 개수 선택 (1 ~ 16)", EditorStyles.boldLabel);
        targetCount = EditorGUILayout.IntSlider("목표 개수", targetCount, 1, 16);

        EditorGUILayout.LabelField("⚡ 빠른 선택 (누르면 즉시 생성/동기화):", EditorStyles.miniLabel);
        DrawQuickButtons();

        EditorGUILayout.Space(10);

        // 3. 배치 간격 설정
        EditorGUILayout.LabelField("3. 배치 설정", EditorStyles.boldLabel);
        spacing = EditorGUILayout.FloatField("환경 간격 (Spacing)", spacing);
        if (spacing < 60f)
        {
            EditorGUILayout.HelpBox("간격이 너무 좁으면(60 미만) 레이캐스트 센서가 인접 환경을 감지할 수 있습니다. 80~100 이상을 권장합니다.", MessageType.Warning);
        }

        columns = EditorGUILayout.IntField("가로 열 개수 (0 = 자동)", columns);
        int effectiveCols = columns > 0 ? columns : Mathf.CeilToInt(Mathf.Sqrt(targetCount));
        int effectiveRows = Mathf.CeilToInt((float)targetCount / effectiveCols);
        EditorGUILayout.LabelField($"배치 형태: {effectiveCols}열 x {effectiveRows}행 (간격 {spacing}m)", EditorStyles.miniLabel);

        EditorGUILayout.Space(15);

        // 4. 실행 버튼
        GUI.backgroundColor = new Color(0.35f, 0.75f, 1.0f);
        if (GUILayout.Button($"🚀 {targetCount}개 환경으로 생성 / 동기화", GUILayout.Height(38)))
        {
            ApplyDuplication(targetCount);
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(5);

        GUI.backgroundColor = new Color(1.0f, 0.5f, 0.5f);
        if (GUILayout.Button("🗑️ 복제본 모두 삭제 (원본 1개로 초기화)", GUILayout.Height(28)))
        {
            if (EditorUtility.DisplayDialog("초기화 확인", "모든 복제본을 삭제하고 원본 1개만 남기시겠습니까?", "삭제", "취소"))
            {
                ApplyDuplication(1);
            }
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.EndScrollView();
    }

    /// <summary>
    /// 자주 사용하는 환경 개수(1, 2, 4, 6, 8, 9, 12, 16)를 원클릭으로 선택하는 빠른 버튼들을 그립니다.
    /// </summary>
    private void DrawQuickButtons()
    {
        int[] counts = { 1, 2, 4, 6, 8, 9, 12, 16 };
        EditorGUILayout.BeginHorizontal();
        for (int i = 0; i < counts.Length; i++)
        {
            int c = counts[i];
            if (GUILayout.Button($"{c}개", GUILayout.Height(26)))
            {
                targetCount = c;
                ApplyDuplication(c);
                GUIUtility.ExitGUI();
            }
            if (i == 3)
            {
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// 원본 MazeArea를 기준으로 지정된 개수(count)의 환경을 복제하고 격자 형태로 정렬합니다.
    /// 기존 복제본을 안전하게 제거한 뒤 최신 상태의 원본을 새로 복제하여 완전한 동기화를 보장합니다.
    /// </summary>
    /// <param name="count">생성/유지할 총 환경 개수 (1 = 원본만 유지)</param>
    private void ApplyDuplication(int count)
    {
        if (sourceArea == null)
        {
            EditorUtility.DisplayDialog("오류", "원본 MazeArea가 지정되지 않았습니다.", "확인");
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName($"Duplicate MazeArea to {count}");

        // 1. 기존 복제본 정리
        List<MazeArea> existingClones = GetCurrentClones();
        for (int i = 0; i < existingClones.Count; i++)
        {
            if (existingClones[i] != null && existingClones[i] != sourceArea)
            {
                Undo.DestroyObjectImmediate(existingClones[i].gameObject);
            }
        }

        // 2. 원본 위치를 (0, 0, 0) 기준으로 정렬
        Undo.RecordObject(sourceArea.transform, "Align Source MazeArea Position");
        sourceArea.transform.position = Vector3.zero;
        sourceArea.name = "MazeArea";
        sourceArea.SetupReferences();
        EditorUtility.SetDirty(sourceArea);

        // 1개만 요청한 경우 여기서 종료
        if (count <= 1)
        {
            EditorSceneManager.MarkSceneDirty(sourceArea.gameObject.scene);
            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log("[MazeAreaDuplicator] ✅ 원본 1개만 남기고 모든 복제본을 삭제했습니다.");
            return;
        }

        // 3. 복제 및 격자 배치
        int cols = columns > 0 ? columns : Mathf.CeilToInt(Mathf.Sqrt(count));

        for (int i = 1; i < count; i++)
        {
            int col = i % cols;
            int row = i / cols;
            Vector3 pos = new Vector3(col * spacing, 0f, row * spacing);

            GameObject cloneObj = Instantiate(sourceArea.gameObject, pos, sourceArea.transform.rotation);
            cloneObj.name = $"{ClonePrefix}{i + 1}";
            Undo.RegisterCreatedObjectUndo(cloneObj, "Create MazeArea Clone");

            MazeArea cloneArea = cloneObj.GetComponent<MazeArea>();
            if (cloneArea != null)
            {
                // 자식 오브젝트 참조 완벽 재연결
                cloneArea.player = cloneObj.GetComponentInChildren<PlayerAgent>(true);
                cloneArea.enemy = cloneObj.GetComponentInChildren<EnemyAgent>(true);

                cloneArea.keys = new List<KeyItem>(cloneObj.GetComponentsInChildren<KeyItem>(true));
                cloneArea.doors = new List<EscapeDoor>(cloneObj.GetComponentsInChildren<EscapeDoor>(true));
                cloneArea.boxes = new List<MovableBox>();
                foreach (var b in cloneObj.GetComponentsInChildren<MovableBox>(true))
                {
                    if (b.gameObject.name != "Cubes") cloneArea.boxes.Add(b);
                }

                cloneArea.SetupReferences();
                EditorUtility.SetDirty(cloneArea);
            }

            EditorUtility.SetDirty(cloneObj);
        }

        EditorSceneManager.MarkSceneDirty(sourceArea.gameObject.scene);
        Undo.CollapseUndoOperations(undoGroup);

        Debug.Log($"[MazeAreaDuplicator] ✅ 총 {count}개의 환경이 최신 원본 상태로 생성/동기화되었습니다. (간격: {spacing}m, {cols}열)");
    }

    /// <summary>
    /// 씬에서 "MazeArea_Clone_" 접두사가 붙지 않은 원본 템플릿 MazeArea를 검색합니다.
    /// </summary>
    private void FindOrCreateSourceArea()
    {
        MazeArea[] allAreas = Object.FindObjectsByType<MazeArea>(FindObjectsSortMode.None);
        for (int i = 0; i < allAreas.Length; i++)
        {
            if (allAreas[i] != null && !allAreas[i].name.StartsWith(ClonePrefix))
            {
                sourceArea = allAreas[i];
                return;
            }
        }

        if (allAreas.Length > 0 && sourceArea == null)
        {
            sourceArea = allAreas[0];
        }
    }

    /// <summary>
    /// 현재 씬에 존재하는 모든 복제본 MazeArea 목록을 반환합니다.
    /// </summary>
    /// <returns>복제본 MazeArea 리스트</returns>
    private List<MazeArea> GetCurrentClones()
    {
        List<MazeArea> list = new List<MazeArea>();
        MazeArea[] allAreas = Object.FindObjectsByType<MazeArea>(FindObjectsSortMode.None);
        for (int i = 0; i < allAreas.Length; i++)
        {
            if (allAreas[i] != null && allAreas[i] != sourceArea && allAreas[i].name.StartsWith(ClonePrefix))
            {
                list.Add(allAreas[i]);
            }
        }
        return list;
    }
}
