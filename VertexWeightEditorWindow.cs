using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace VertexWeightTool
{
    public class VertexWeightEditorWindow : EditorWindow
    {
        [MenuItem("Tools/Vertex Weight Adjust Tool")]
        public static void ShowWindow()
        {
            GetWindow<VertexWeightEditorWindow>("Vertex Weight Tool");
        }

        // 状態変数
        private bool isEditMode = false;
        private Transform currentSelection;
        private SkinnedMeshRenderer targetSkinnedMesh;
        private Mesh targetMesh;
        private int selectedVertexIndex = -1;
        private Vector3 selectedVertexPos;
        
        // 編集用データ
        private List<BoneWeightInfo> currentBoneWeights = new List<BoneWeightInfo>();
        // _clipboardWeights declared below

        // GUI定数
        private const float HANDLE_SIZE = 0.05f;
        private const float PICK_SIZE = 0.1f;

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        private void OnUndoRedo()
        {
            // Undo時に値表示を更新するために再取得
            if (targetSkinnedMesh != null && selectedVertexIndex != -1)
            {
                RefreshVertexData();
                Repaint();
            }
        }

        private void OnGUI()
        {
            GUILayout.Label("Vertex Weight Adjust Tool", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            isEditMode = GUILayout.Toggle(isEditMode, "Edit Mode", "Button", GUILayout.Height(30));
            if (EditorGUI.EndChangeCheck())
            {
                SceneView.RepaintAll();
            }

            if (!isEditMode)
            {
                EditorGUILayout.HelpBox("Select a SkinnedMeshRenderer and enable Edit Mode to start.", MessageType.Info);
                return;
            }

            ValidateSelection();

            if (targetSkinnedMesh == null)
            {
                EditorGUILayout.HelpBox("Please select a GameObject with SkinnedMeshRenderer.", MessageType.Warning);
                return;
            }

            GUILayout.Space(10);
            
            if (selectedVertexIndex != -1)
            {
                DrawSelectedVertexInfo();
            }
            else
            {
                GUILayout.Label("No vertex selected. Click a vertex in Scene View.");
            }
        }

        private void ValidateSelection()
        {
            Transform activeTransform = Selection.activeTransform;
            if (activeTransform != currentSelection)
            {
                currentSelection = activeTransform;
                if (currentSelection != null)
                {
                    targetSkinnedMesh = currentSelection.GetComponent<SkinnedMeshRenderer>();
                    if (targetSkinnedMesh != null)
                    {
                        targetMesh = targetSkinnedMesh.sharedMesh;
                    }
                    else
                    {
                        targetMesh = null;
                        selectedVertexIndex = -1;
                    }
                }
                else
                {
                    targetSkinnedMesh = null;
                    targetMesh = null;
                    selectedVertexIndex = -1;
                }
                Repaint();
            }
        }

        private void DrawSelectedVertexInfo()
        {
            GUILayout.Label($"Selected Vertex: {selectedVertexIndex}", EditorStyles.boldLabel);

            // コピー＆ペースト
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy Weights"))
            {
                CopyWeights();
            }
            EditorGUI.BeginDisabledGroup(_clipboardWeights == null || _clipboardWeights.Count == 0);
            if (GUILayout.Button("Paste Weights"))
            {
                PasteWeights();
            }
            EditorGUI.EndDisabledGroup();
            GUILayout.EndHorizontal();

            if (_clipboardWeights != null && _clipboardWeights.Count > 0)
            {
                GUILayout.Label($"Clipboard: {_clipboardWeights.Count} bones", EditorStyles.miniLabel);
            }

            GUILayout.Space(10);
            GUILayout.Label("Weights Distribution", EditorStyles.label);

            // スライダー描画
            Rect sliderRect = GUILayoutUtility.GetRect(position.width - 20, 30);
            // 余白調整
            sliderRect.x += 10;
            sliderRect.width -= 20;

            if (WeightPartitionSlider.Draw(sliderRect, currentBoneWeights))
            {
                ApplyWeights();
            }

            GUILayout.Space(20);
            GUILayout.Label("Details:", EditorStyles.boldLabel);

            // 数値直接編集用
            bool changed = false;
            foreach (var bw in currentBoneWeights)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(bw.boneName, GUILayout.Width(150));
                
                float newWeight = EditorGUILayout.FloatField(bw.weight);
                if (!Mathf.Approximately(newWeight, bw.weight))
                {
                    bw.weight = newWeight;
                    changed = true;
                }
                GUILayout.EndHorizontal();
            }

            if (changed)
            {
                NormalizeCurrentWeights();
                ApplyWeights();
            }
        }
        
        private void NormalizeCurrentWeights()
        {
            float total = currentBoneWeights.Sum(w => w.weight);
            if (total > 0 && Mathf.Abs(total - 1f) > 0.0001f)
            {
                foreach(var bw in currentBoneWeights)
                {
                    bw.weight /= total;
                }
            }
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!isEditMode || targetSkinnedMesh == null || targetMesh == null) return;

            Event e = Event.current;
            
            // 頂点の可視化と選択
            // パフォーマンスのため、マウスに近い頂点だけを描画・判定するロジックが必要
            // 簡易実装として、全頂点をハンドルとして描画するのは重すぎるため、
            // マウス位置からRayを飛ばして近い頂点を探索する

            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                // クリック判定
                int nearestIndex = FindNearestVertex(e.mousePosition);
                if (nearestIndex != -1)
                {
                    selectedVertexIndex = nearestIndex;
                    RefreshVertexData();
                    Repaint();
                    e.Use(); // イベント消費
                }
            }

            // 選択頂点の描画
            if (selectedVertexIndex != -1 && selectedVertexIndex < targetMesh.vertexCount)
            {
                Vector3 worldPos = targetSkinnedMesh.transform.TransformPoint(targetMesh.vertices[selectedVertexIndex]);
                Handles.color = Color.green;
                Handles.DotHandleCap(0, worldPos, Quaternion.identity, HandleUtility.GetHandleSize(worldPos) * 0.1f, EventType.Repaint);
            }

            // マウスオーバーのハイライト（オプション）
            // 重い場合は省略
        }

        private int FindNearestVertex(Vector2 mousePos)
        {
            if (targetMesh == null) return -1;

            Vector3[] vertices = targetMesh.vertices;
            Transform tr = targetSkinnedMesh.transform;
            
            float minDist = 30f; // ピクセル単位の閾値
            int bestIndex = -1;

            // 全探索は頂点数が多いと重いが、SceneGUI内でどこまで許容されるか
            // 一旦シンプルに実装し、重ければカメラ視錐台カリングやKDTreeなどを検討
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 worldPos = tr.TransformPoint(vertices[i]);
                Vector2 screenPos = HandleUtility.WorldToGUIPoint(worldPos);
                
                float dist = Vector2.Distance(screenPos, mousePos);
                if (dist < minDist)
                {
                    minDist = dist;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private void RefreshVertexData()
        {
            if (targetMesh == null || selectedVertexIndex == -1) return;

            BoneWeight bw = targetMesh.boneWeights[selectedVertexIndex];
            currentBoneWeights.Clear();

            // BoneWeight構造体は4つのボーンまで
            AddBoneWeightInfo(bw.boneIndex0, bw.weight0);
            AddBoneWeightInfo(bw.boneIndex1, bw.weight1);
            AddBoneWeightInfo(bw.boneIndex2, bw.weight2);
            AddBoneWeightInfo(bw.boneIndex3, bw.weight3);
            
            // 重みが0のものは表示しない方針か、リストに残すか
            // 微調整ツールなので0のものもリストには含めず、追加ボタンで足す形式が一般的だが、
            // 今回はシンプルに非ゼロのみ表示し、合計1になるようにする
            currentBoneWeights.RemoveAll(x => x.weight <= 0.0001f);
            
            // 正規化チェック
            float total = currentBoneWeights.Sum(x => x.weight);
            if (total > 0 && Mathf.Abs(1.0f - total) > 0.001f)
            {
                // 表示用に正規化しておく（データ自体はそのまま）
                // ただし編集時は正規化した値を書き込む
            }
        }

        // クリップボード用リスト
        private List<BoneWeightInfo> _clipboardWeights = new List<BoneWeightInfo>();

        private void CopyWeights()
        {
            if (currentBoneWeights == null) return;
            _clipboardWeights = currentBoneWeights.Select(w => new BoneWeightInfo(w.boneIndex, w.boneName, w.weight)).ToList();
            Debug.Log($"Copied weights from vertex {selectedVertexIndex}");
        }

        private void PasteWeights()
        {
            if (_clipboardWeights == null || _clipboardWeights.Count == 0 || selectedVertexIndex == -1) return;
            
            // 現在のリストをクリップボードの内容で置き換え (Deep Copy)
            currentBoneWeights = _clipboardWeights.Select(w => new BoneWeightInfo(w.boneIndex, w.boneName, w.weight)).ToList();
            
            ApplyWeights();
            Repaint();
            Debug.Log($"Pasted weights to vertex {selectedVertexIndex}");
        }

        private void AddBoneWeightInfo(int boneIndex, float weight)
        {
            if (weight > 0)
            {
                Transform bone = targetSkinnedMesh.bones[boneIndex];
                string name = bone != null ? bone.name : $"Bone {boneIndex}";
                currentBoneWeights.Add(new BoneWeightInfo(boneIndex, name, weight));
            }
        }

        private void ApplyWeights()
        {
            if (targetMesh == null || selectedVertexIndex == -1) return;

            Undo.RecordObject(targetMesh, "Update Vertex Weights");

            BoneWeight[] weights = targetMesh.boneWeights;
            BoneWeight bw = weights[selectedVertexIndex];

            // リストの内容をBoneWeight構造体に書き戻す
            // 上位4つを採用する（Unityの制限）
            var sorted = currentBoneWeights.OrderByDescending(w => w.weight).Take(4).ToList();
            
            // 一旦ゼロクリア
            bw.boneIndex0 = 0; bw.weight0 = 0;
            bw.boneIndex1 = 0; bw.weight1 = 0;
            bw.boneIndex2 = 0; bw.weight2 = 0;
            bw.boneIndex3 = 0; bw.weight3 = 0;

            if (sorted.Count > 0) { bw.boneIndex0 = sorted[0].boneIndex; bw.weight0 = sorted[0].weight; }
            if (sorted.Count > 1) { bw.boneIndex1 = sorted[1].boneIndex; bw.weight1 = sorted[1].weight; }
            if (sorted.Count > 2) { bw.boneIndex2 = sorted[2].boneIndex; bw.weight2 = sorted[2].weight; }
            if (sorted.Count > 3) { bw.boneIndex3 = sorted[3].boneIndex; bw.weight3 = sorted[3].weight; }

            weights[selectedVertexIndex] = bw;
            targetMesh.boneWeights = weights; // 配列ごと再代入が必要
            
            EditorUtility.SetDirty(targetMesh); // アセットを保存対象としてマーク
            
            // 即時反映のためにDirty設定などは不要だが、
            // SkinnedMeshRendererへの通知が必要な場合がある（通常はsharedMesh書き換えでOK）
        }
    }
}
