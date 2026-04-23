#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomEditor(typeof(BatonSwingData))]
public class BatonSwingDataEditor : Editor
{
    private ReorderableList _list;
    private SerializedProperty _keyframesProp;

    // scene gizmo reference space: BatonAttackDriver._visualRoot.parent
    private static Transform _referenceSpace;

    private void OnEnable()
    {
        _keyframesProp = serializedObject.FindProperty("keyframes");
        BuildReorderableList();
        SceneView.duringSceneGui += OnSceneGUI;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(serializedObject.FindProperty("totalDuration"));
        EditorGUILayout.Space(4);

        EditorGUILayout.PropertyField(serializedObject.FindProperty("springStiffness"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("springDamping"));
        EditorGUILayout.Space(4);

        EditorGUILayout.PropertyField(serializedObject.FindProperty("impactShakeFrequency"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("impactShakeDecay"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("impactShakeAmplitude"));
        EditorGUILayout.Space(6);

        _list.DoLayoutList();

        serializedObject.ApplyModifiedProperties();
    }

    private void BuildReorderableList()
    {
        _list = new ReorderableList(serializedObject, _keyframesProp,
            draggable: true, displayHeader: true,
            displayAddButton: true, displayRemoveButton: true);

        _list.drawHeaderCallback = rect =>
            EditorGUI.LabelField(rect, "Keyframes (normalizedTime must be ascending)", EditorStyles.boldLabel);

        _list.elementHeightCallback = index =>
        {
            var elem = _keyframesProp.GetArrayElementAtIndex(index);
            bool isImpact = elem.FindPropertyRelative("isImpactFrame").boolValue;
            int lines = isImpact ? 9 : 7;
            return EditorGUIUtility.singleLineHeight * lines + 6f * lines;
        };

        _list.drawElementCallback = (rect, index, isActive, isFocused) =>
        {
            var elem = _keyframesProp.GetArrayElementAtIndex(index);
            rect.y += 2;
            float lh = EditorGUIUtility.singleLineHeight + 4f;

            Rect Line(int lineIndex) => new Rect(rect.x, rect.y + lh * lineIndex, rect.width, EditorGUIUtility.singleLineHeight);

            EditorGUI.PropertyField(Line(0), elem.FindPropertyRelative("normalizedTime"),
                new GUIContent($"[{index}] Normalized Time"));
            EditorGUI.PropertyField(Line(1), elem.FindPropertyRelative("localPosition"),
                new GUIContent("Local Position"));
            EditorGUI.PropertyField(Line(2), elem.FindPropertyRelative("localEulerAngles"),
                new GUIContent("Local Rotation (Euler)"));
            EditorGUI.PropertyField(Line(3), elem.FindPropertyRelative("positionEase"),
                new GUIContent("Position Ease (to next)"));
            EditorGUI.PropertyField(Line(4), elem.FindPropertyRelative("rotationEase"),
                new GUIContent("Rotation Ease (to next)"));

            var isImpactProp = elem.FindPropertyRelative("isImpactFrame");
            EditorGUI.PropertyField(Line(5), isImpactProp, new GUIContent("Impact Frame"));

            if (isImpactProp.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUI.PropertyField(Line(6), elem.FindPropertyRelative("hitstopDuration"),
                    new GUIContent("Hitstop Duration (s)"));
                EditorGUI.PropertyField(Line(7), elem.FindPropertyRelative("shakeDuration"),
                    new GUIContent("Shake Duration (s)"));
                EditorGUI.indentLevel--;
            }
        };

        _list.onAddCallback = l =>
        {
            l.serializedProperty.arraySize++;
            var elem = l.serializedProperty.GetArrayElementAtIndex(l.serializedProperty.arraySize - 1);
            elem.FindPropertyRelative("normalizedTime").floatValue = 1f;
            elem.FindPropertyRelative("localPosition").vector3Value = Vector3.zero;
            elem.FindPropertyRelative("localEulerAngles").vector3Value = Vector3.zero;
            elem.FindPropertyRelative("isImpactFrame").boolValue = false;
            elem.FindPropertyRelative("hitstopDuration").floatValue = 0f;
            elem.FindPropertyRelative("shakeDuration").floatValue = 0f;
        };
    }

    private void OnSceneGUI(SceneView sceneView)
    {
        var data = (BatonSwingData)target;
        if (data == null || data.keyframes == null || data.keyframes.Length == 0) return;

        RefreshReferenceSpace();

        Matrix4x4 localToWorld = _referenceSpace != null
            ? _referenceSpace.localToWorldMatrix
            : Matrix4x4.identity;

        Vector3[] worldPositions = new Vector3[data.keyframes.Length];
        for (int i = 0; i < data.keyframes.Length; i++)
            worldPositions[i] = localToWorld.MultiplyPoint3x4(data.keyframes[i].localPosition);

        Handles.color = new Color(0.4f, 0.8f, 1f, 0.6f);
        for (int i = 0; i < worldPositions.Length - 1; i++)
            Handles.DrawLine(worldPositions[i], worldPositions[i + 1], 2f);

        for (int i = 0; i < data.keyframes.Length; i++)
        {
            bool isImpact = data.keyframes[i].isImpactFrame;
            Handles.color = isImpact ? new Color(1f, 0.3f, 0.2f, 0.9f) : new Color(0.3f, 0.9f, 0.5f, 0.8f);

            float size = HandleUtility.GetHandleSize(worldPositions[i]) * 0.08f;
            Handles.SphereHandleCap(0, worldPositions[i], Quaternion.identity, size, EventType.Repaint);

            Handles.Label(worldPositions[i] + Vector3.up * size * 1.4f,
                $"[{i}] t={data.keyframes[i].normalizedTime:F2}" + (isImpact ? " *" : ""),
                EditorStyles.miniLabel);

            EditorGUI.BeginChangeCheck();
            Vector3 newWorld = Handles.PositionHandle(worldPositions[i],
                _referenceSpace != null ? _referenceSpace.rotation : Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(data, "Move SwingKeyframe");
                var kf = data.keyframes[i];
                kf.localPosition = localToWorld.inverse.MultiplyPoint3x4(newWorld);
                data.keyframes[i] = kf;
                EditorUtility.SetDirty(data);
            }

            // only show rotation handle for selected element to reduce clutter
            if (_list != null && _list.index == i)
            {
                Quaternion worldRot = (_referenceSpace != null ? _referenceSpace.rotation : Quaternion.identity)
                    * Quaternion.Euler(data.keyframes[i].localEulerAngles);

                EditorGUI.BeginChangeCheck();
                Quaternion newWorldRot = Handles.RotationHandle(worldRot, worldPositions[i]);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(data, "Rotate SwingKeyframe");
                    Quaternion parentInv = _referenceSpace != null
                        ? Quaternion.Inverse(_referenceSpace.rotation)
                        : Quaternion.identity;
                    var kf = data.keyframes[i];
                    kf.localEulerAngles = (parentInv * newWorldRot).eulerAngles;
                    data.keyframes[i] = kf;
                    EditorUtility.SetDirty(data);
                }
            }
        }

        sceneView.Repaint();
    }

    private static void RefreshReferenceSpace()
    {
        if (_referenceSpace != null) return;

        var driver = Object.FindFirstObjectByType<BatonAttackDriver>();
        if (driver == null) return;

        // _visualRoot is private, read via SerializedObject
        var so = new SerializedObject(driver);
        var visualRootProp = so.FindProperty("_visualRoot");
        if (visualRootProp == null) return;

        var visualRoot = visualRootProp.objectReferenceValue as Transform;
        if (visualRoot != null && visualRoot.parent != null)
            _referenceSpace = visualRoot.parent;
        else if (visualRoot != null)
            _referenceSpace = visualRoot;
    }
}
#endif
