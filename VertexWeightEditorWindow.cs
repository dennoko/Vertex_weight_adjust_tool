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

        private bool isEditMode = false;
        private bool showAllVertices = false;
        private bool showOccludedVertices = true;
        private bool showHeatmap = false;
        private int heatmapBoneIndex = -1; // 選択中のボーンインデックス
        // private Transform currentSelection; // Removed dependency on selection
        private SkinnedMeshRenderer targetSkinnedMesh;
        private Mesh targetMesh;
        private int selectedVertexIndex = -1;
        private Vector3 selectedVertexPos;
        
        // 編集用データ
        private List<BoneWeightInfo> currentBoneWeights = new List<BoneWeightInfo>();
        // _clipboardWeights declared below

        // 設定
        private Color vertexColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        private Color selectedVertexColor = Color.green;
        
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
            
            if (isEditMode)
            {
                GUILayout.BeginHorizontal();
                showAllVertices = GUILayout.Toggle(showAllVertices, "Show All Vertices");
                if (showAllVertices)
                {
                    showOccludedVertices = GUILayout.Toggle(showOccludedVertices, "Show Occluded");
                }
                
                GUILayout.Space(10);
                showHeatmap = GUILayout.Toggle(showHeatmap, "Show Heatmap");
                if (showHeatmap)
                {
                    // ヒートマップ対象ボーンの選択
                    string[] boneNames = targetSkinnedMesh != null ? targetSkinnedMesh.bones.Select(b => b.name).ToArray() : new string[0];
                    if (boneNames.Length > 0)
                    {
                        int selectedIndexInList = -1;
                        if (heatmapBoneIndex != -1) 
                        {
                            selectedIndexInList = heatmapBoneIndex;
                        }
                        
                        int newIndex = EditorGUILayout.Popup("Bone", selectedIndexInList, boneNames);
                        if (newIndex != selectedIndexInList)
                        {
                            heatmapBoneIndex = newIndex;
                            SceneView.RepaintAll();
                        }
                    }
                }
                GUILayout.EndHorizontal();
                
                // Colors
                GUILayout.BeginHorizontal();
                vertexColor = EditorGUILayout.ColorField("Vertex Color", vertexColor);
                selectedVertexColor = EditorGUILayout.ColorField("Selected Color", selectedVertexColor);
                GUILayout.EndHorizontal();
            }

            if (EditorGUI.EndChangeCheck())
            {
                SceneView.RepaintAll();
            }

            GUILayout.Space(5);
            
            // Target Selection (Explicit)
            EditorGUI.BeginChangeCheck();
            targetSkinnedMesh = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("Target Mesh", targetSkinnedMesh, typeof(SkinnedMeshRenderer), true);
            if (EditorGUI.EndChangeCheck())
            {
                if (targetSkinnedMesh != null)
                {
                    targetMesh = targetSkinnedMesh.sharedMesh;
                }
                else
                {
                    targetMesh = null;
                }
                selectedVertexIndex = -1;
                currentBoneWeights.Clear();
                isEditMode = true; // AssignしたタイミングでONにする便利機能
                SceneView.RepaintAll();
            }

            // Ensure targetMesh is assigned if reference exists (e.g. after recompile)
            if (targetSkinnedMesh != null && targetMesh == null)
            {
                targetMesh = targetSkinnedMesh.sharedMesh;
            }

            if (!isEditMode)
            {
                EditorGUILayout.HelpBox("Enable Edit Mode to start.", MessageType.Info);
                return;
            }

            if (targetSkinnedMesh == null)
            {
                EditorGUILayout.HelpBox("Please assign a SkinnedMeshRenderer.", MessageType.Warning);
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


        private void DrawSelectedVertexInfo()
        {
            GUILayout.Label($"Selected Vertex: {selectedVertexIndex}", EditorStyles.boldLabel);
            
            GUILayout.BeginHorizontal();
            // Prune 機能
            if (GUILayout.Button("Prune < 0.01"))
            {
                PruneWeights(0.01f);
            }
            // Mirror 機能
            if (GUILayout.Button("Mirror Weights (X-Axis)"))
            {
                MirrorWeights();
            }
            GUILayout.EndHorizontal();

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
                
                // Lock Toggle
                bw.isLocked = EditorGUILayout.Toggle(bw.isLocked, GUILayout.Width(20));

                GUILayout.Label(bw.boneName, GUILayout.Width(130));
                
                EditorGUI.BeginDisabledGroup(bw.isLocked);
                float newWeight = EditorGUILayout.FloatField(bw.weight);
                if (!bw.isLocked && !Mathf.Approximately(newWeight, bw.weight))
                {
                    bw.weight = newWeight;
                    changed = true;
                }
                EditorGUI.EndDisabledGroup();
                
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
            // ロックされていないものの和を計算
            float lockedTotal = currentBoneWeights.Where(w => w.isLocked).Sum(w => w.weight);
            var unlockedBones = currentBoneWeights.Where(w => !w.isLocked).ToList();
            
            if (lockedTotal >= 1.0f)
            {
                // ロックだけで埋まっている場合、他は0にする
                foreach(var bw in unlockedBones) bw.weight = 0f;
                return;
            }

            float targetUnlockedTotal = 1.0f - lockedTotal;
            float currentUnlockedTotal = unlockedBones.Sum(w => w.weight);

            if (currentUnlockedTotal > 0.0001f)
            {
                float scale = targetUnlockedTotal / currentUnlockedTotal;
                foreach(var bw in unlockedBones)
                {
                    bw.weight *= scale;
                }
            }
            else
            {
                // 全て0だった場合など -> 均等配分などで埋める？
                // とりあえず何もしないか、最初の要素に残りを振る
                if (unlockedBones.Count > 0)
                {
                    unlockedBones[0].weight = targetUnlockedTotal;
                }
            }
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!isEditMode || targetSkinnedMesh == null || targetMesh == null) return;

            Event e = Event.current;

            // 頂点描画
            if (e.type == EventType.Repaint)
            {
                DrawVertices();
            }
            
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                // クリック判定 (既存ロジック)
                int nearestIndex = FindNearestVertex(e.mousePosition);
                if (nearestIndex != -1)
                {
                    selectedVertexIndex = nearestIndex;
                    RefreshVertexData();
                    Repaint();
                    e.Use(); // イベント消費
                }
            }

        }

        private void DrawVertices()
        {
            if (targetMesh == null || targetSkinnedMesh == null) return;

            // カメラ情報の取得
            Camera cam = SceneView.currentDrawingSceneView.camera;
            if (cam == null) return;

            Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(cam);
            
            Transform tr = targetSkinnedMesh.transform;
            Matrix4x4 localToWorld = tr.localToWorldMatrix;
            Vector3[] vertices = targetMesh.vertices;
            
            // 描画設定
            var originalZTest = Handles.zTest;
            Handles.zTest = showOccludedVertices ? UnityEngine.Rendering.CompareFunction.Always : UnityEngine.Rendering.CompareFunction.LessEqual;

            // ヒートマップ用データの事前準備（重い場合はキャッシュ検討）
            Dictionary<int, float> heatmapWeights = null;
            if (showHeatmap && heatmapBoneIndex != -1)
            {
                 // 全頂点の該当ボーンウェイトを取得するのは重いので、
                 // DrawLoop内で BoneWeights 配列にアクセスする
                 // ただし targetMesh.boneWeights はコピーを返すので、事前に取得しておく
            }
            BoneWeight[] meshBoneWeights = targetMesh.boneWeights; // 配列コピーが発生するプロパティ

            // 選択中の頂点を描画 (常に表示)
            if (selectedVertexIndex != -1 && selectedVertexIndex < vertices.Length)
            {
                Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
                Vector3 worldPos = localToWorld.MultiplyPoint3x4(vertices[selectedVertexIndex]);
                Handles.color = selectedVertexColor;
                Handles.DotHandleCap(0, worldPos, Quaternion.identity, HandleUtility.GetHandleSize(worldPos) * 0.08f, EventType.Repaint);
            }

            // 全表示の場合の描画
            if (showAllVertices)
            {
                // Color ramp
                Color baseColor = vertexColor;

                int len = vertices.Length;
                for (int i = 0; i < len; i++)
                {
                    if (i == selectedVertexIndex) continue;

                    Vector3 worldPos = localToWorld.MultiplyPoint3x4(vertices[i]);
                    
                    // カリング
                    Vector3 viewportPos = cam.WorldToViewportPoint(worldPos);
                    if (viewportPos.x < 0 || viewportPos.x > 1 || viewportPos.y < 0 || viewportPos.y > 1 || viewportPos.z < 0)
                    {
                        continue; // 画面外
                    }

                    // 色決定
                    if (showHeatmap && heatmapBoneIndex != -1)
                    {
                        float w = GetBoneWeight(meshBoneWeights[i], heatmapBoneIndex);
                        // Blue(0) -> Red(1)
                        // 0の場合は表示しない、あるいは薄く
                        if (w <= 0.001f) Handles.color = baseColor;
                        else Handles.color = Color.Lerp(Color.blue, Color.red, w);
                    }
                    else
                    {
                        Handles.color = baseColor;
                    }

                    // 描画
                    float size = HandleUtility.GetHandleSize(worldPos) * 0.03f;
                    Handles.DotHandleCap(0, worldPos, Quaternion.identity, size, EventType.Repaint);
                }
            }
            
            Handles.zTest = originalZTest;
        }

        private float GetBoneWeight(BoneWeight bw, int boneIndex)
        {
            if (bw.boneIndex0 == boneIndex) return bw.weight0;
            if (bw.boneIndex1 == boneIndex) return bw.weight1;
            if (bw.boneIndex2 == boneIndex) return bw.weight2;
            if (bw.boneIndex3 == boneIndex) return bw.weight3;
            return 0f;
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
            // Lock状態はコピーしない方が安全かもしれないが、一応コピーする？今回はしないでおく
            _clipboardWeights = currentBoneWeights.Select(w => new BoneWeightInfo(w.boneIndex, w.boneName, w.weight)).ToList();
            Debug.Log($"Copied weights from vertex {selectedVertexIndex}");
        }

        private void PasteWeights()
        {
            if (_clipboardWeights == null || _clipboardWeights.Count == 0 || selectedVertexIndex == -1) return;
            
            // Paste時はLock情報はリセットする（新しい値になるため）
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
            
            EditorUtility.SetDirty(targetMesh); // アセットを保存対象としてマーク
            
            // 即時反映のためにDirty設定などは不要だが、
            // SkinnedMeshRendererへの通知が必要な場合がある（通常はsharedMesh書き換えでOK）
        }

        private void PruneWeights(float threshold)
        {
            if (currentBoneWeights == null) return;
            currentBoneWeights.RemoveAll(w => w.weight < threshold);
            NormalizeCurrentWeights();
            ApplyWeights();
            Repaint();
            Debug.Log("Pruned small weights.");
        }

        private void MirrorWeights()
        {
            if (targetMesh == null || selectedVertexIndex == -1) return;
            
            Transform tr = targetSkinnedMesh.transform;
            Vector3 currentPosLocal = targetMesh.vertices[selectedVertexIndex];
            Vector3 mirrorPosLocal = new Vector3(-currentPosLocal.x, currentPosLocal.y, currentPosLocal.z); 
            // ワールド座標系でのミラーが必要なら変換挟むが、通常モデルはローカルで対象

            // 対称頂点を探す
            int mirrorIndex = FindNearestVertexLocal(mirrorPosLocal);
            if (mirrorIndex == -1 || mirrorIndex == selectedVertexIndex)
            {
                Debug.LogWarning("Mirror vertex not found.");
                return;
            }

            // ボーン名のマッピング
            List<BoneWeightInfo> mirroredWeights = new List<BoneWeightInfo>();
            foreach (var bw in currentBoneWeights)
            {
                string mirrorName = GetMirrorBoneName(bw.boneName);
                if (string.IsNullOrEmpty(mirrorName)) continue;
                
                // ボーンインデックスを探す
                int mirrorBoneIndex = FindBoneIndex(mirrorName);
                if (mirrorBoneIndex != -1)
                {
                    mirroredWeights.Add(new BoneWeightInfo(mirrorBoneIndex, mirrorName, bw.weight));
                }
                else
                {
                    Debug.LogWarning($"Mirror bone not found: {mirrorName}");
                }
            }

            // 適用 (対象頂点を選択してApplyするわけではないので、直接BoneWeightsを書き換える)
            ApplyWeightsToVertex(mirrorIndex, mirroredWeights);
            Debug.Log($"Mirrored weights from {selectedVertexIndex} to {mirrorIndex}");
        }

        private int FindNearestVertexLocal(Vector3 localPos)
        {
            float minSqrDist = float.MaxValue;
            int bestIndex = -1;
            Vector3[] vertices = targetMesh.vertices;
            
            for(int i=0; i<vertices.Length; i++)
            {
                float sqrDist = (vertices[i] - localPos).sqrMagnitude;
                if(sqrDist < 0.0001f) // かなり近い
                {
                    if (sqrDist < minSqrDist)
                    {
                        minSqrDist = sqrDist;
                        bestIndex = i;
                    }
                }
            }
            return bestIndex;
        }

        private string GetMirrorBoneName(string name)
        {
            // 簡易的な置換ロジック
            if (name.Contains("Left")) return name.Replace("Left", "Right");
            if (name.Contains("Right")) return name.Replace("Right", "Left");
            if (name.Contains("_L")) return name.Replace("_L", "_R");
            if (name.Contains("_R")) return name.Replace("_R", "_L");
            if (name.EndsWith(".L")) return name.Substring(0, name.Length - 2) + ".R";
            if (name.EndsWith(".R")) return name.Substring(0, name.Length - 2) + ".L";
            
            // センターボーンなどはそのまま
            return name;
        }

        private int FindBoneIndex(string name)
        {
            for(int i=0; i<targetSkinnedMesh.bones.Length; i++)
            {
                if (targetSkinnedMesh.bones[i].name == name) return i;
            }
            return -1;
        }

        private void ApplyWeightsToVertex(int vertexIndex, List<BoneWeightInfo> weightsInfos)
        {
            Undo.RecordObject(targetMesh, "Mirror Vertex Weights");
            
            BoneWeight[] weights = targetMesh.boneWeights;
            BoneWeight bw = weights[vertexIndex];

            // Normalize
            float total = weightsInfos.Sum(w => w.weight);
            if (total > 0)
            {
                weightsInfos.ForEach(w => w.weight /= total);
            }

            var sorted = weightsInfos.OrderByDescending(w => w.weight).Take(4).ToList();
            
            bw.boneIndex0 = 0; bw.weight0 = 0;
            bw.boneIndex1 = 0; bw.weight1 = 0;
            bw.boneIndex2 = 0; bw.weight2 = 0;
            bw.boneIndex3 = 0; bw.weight3 = 0;

            if (sorted.Count > 0) { bw.boneIndex0 = sorted[0].boneIndex; bw.weight0 = sorted[0].weight; }
            if (sorted.Count > 1) { bw.boneIndex1 = sorted[1].boneIndex; bw.weight1 = sorted[1].weight; }
            if (sorted.Count > 2) { bw.boneIndex2 = sorted[2].boneIndex; bw.weight2 = sorted[2].weight; }
            if (sorted.Count > 3) { bw.boneIndex3 = sorted[3].boneIndex; bw.weight3 = sorted[3].weight; }

            weights[vertexIndex] = bw;
            targetMesh.boneWeights = weights;
            EditorUtility.SetDirty(targetMesh);
        }
    }
}
