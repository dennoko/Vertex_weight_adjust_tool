using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace VertexWeightTool
{
    public class WeightPartitionSlider
    {
        // 定数
        private const float HANDLE_WIDTH = 10f;
        private const float MIN_WEIGHT_DISPLAY = 0.01f;

        // ドラッグ状態管理
        private static bool isDragging = false;
        private static int draggingSeparatorIndex = -1;
        private static int draggingControlId = -1;

        /// <summary>
        /// ウェイトのパーティションスライダーを描画します。
        /// </summary>
        /// <param name="rect">描画領域</param>
        /// <param name="boneWeights">ボーンごとのウェイト情報のリスト (参照渡しで更新されます)</param>
        /// <returns>値が変更されたかどうか</returns>
        public static bool Draw(Rect rect, List<BoneWeightInfo> boneWeights)
        {
            if (boneWeights == null || boneWeights.Count == 0) return false;

            bool changed = false;
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            Event currentEvent = Event.current;

            // 1. 背景を描画 (念のため)
            EditorGUI.DrawRect(rect, Color.gray);

            // 2. 現在のウェイトに基づいて各セクションを描画
            float currentX = rect.x;
            float totalWidth = rect.width;
            
            // ウェイトの合計が1になるように正規化しておく（念のため）
            NormalizeWeights(boneWeights);

            // 色の生成用
            Color[] colors = new Color[] { 
                new Color(1f, 0.4f, 0.4f), // 赤系
                new Color(0.4f, 1f, 0.4f), // 緑系
                new Color(0.4f, 0.4f, 1f), // 青系
                new Color(1f, 1f, 0.4f)    // 黄系
            };

            List<float> separatorPositions = new List<float>();
            float accumulateWeight = 0f;

            for (int i = 0; i < boneWeights.Count; i++)
            {
                float weight = boneWeights[i].weight;
                float width = weight * totalWidth;
                
                Rect sectionRect = new Rect(currentX, rect.y, width, rect.height);
                Color sectionColor = colors[i % colors.Length];
                
                EditorGUI.DrawRect(sectionRect, sectionColor);
                
                // ボーン名と値を表示（幅が十分ある場合）
                if (width > 30) 
                {
                    string label = $"{boneWeights[i].boneName}\n{weight:P0}";
                    var style = new GUIStyle(EditorStyles.miniLabel);
                    style.alignment = TextAnchor.MiddleCenter;
                    style.normal.textColor = Color.black;
                    GUI.Label(sectionRect, label, style);
                }

                currentX += width;
                accumulateWeight += weight;

                // 最後の要素以外はセパレータ位置を記録
                if (i < boneWeights.Count - 1)
                {
                    separatorPositions.Add(rect.x + (accumulateWeight * totalWidth));
                }
            }

            // 3. セパレータ（ハンドル）の処理
            for (int i = 0; i < separatorPositions.Count; i++)
            {
                // スマートロック判定:
                // 左側(0...i)に少なくとも1つ、右側(i+1...End)に少なくとも1つのUnlockedボーンがあれば操作可能
                bool canExpandLeft = FindUnlockBoneIndex(boneWeights, i, true) != -1;
                bool canExpandRight = FindUnlockBoneIndex(boneWeights, i + 1, false) != -1;

                if (!canExpandLeft || !canExpandRight)
                {
                    float sepXLocked = separatorPositions[i];
                    Rect lockedHandleRect = new Rect(sepXLocked - 1, rect.y, 2, rect.height);
                    EditorGUI.DrawRect(lockedHandleRect, Color.black); 
                    continue;
                }

                float sepX = separatorPositions[i];
                Rect handleRect = new Rect(sepX - HANDLE_WIDTH / 2, rect.y, HANDLE_WIDTH, rect.height);

                EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.ResizeHorizontal);
                
                if (currentEvent.type == EventType.MouseDown && handleRect.Contains(currentEvent.mousePosition))
                {
                    isDragging = true;
                    draggingSeparatorIndex = i;
                    draggingControlId = controlId;
                    currentEvent.Use();
                }
            }

            // 4. ドラッグ処理
            if (isDragging && draggingControlId == controlId)
            {
                if (currentEvent.type == EventType.MouseDrag)
                {
                    float deltaX = currentEvent.delta.x;
                    float deltaWeight = deltaX / totalWidth;
                    
                    int sepIndex = draggingSeparatorIndex;
                    
                    // 操作対象となるUnlockedボーンを探す
                    // 左側は index以下の最も近いUnlocked
                    // 右側は index+1以上の最も近いUnlocked
                    int leftTarget = FindUnlockBoneIndex(boneWeights, sepIndex, true);
                    int rightTarget = FindUnlockBoneIndex(boneWeights, sepIndex + 1, false);

                    if (leftTarget != -1 && rightTarget != -1)
                    {
                        AdjustSmartWeights(boneWeights, leftTarget, rightTarget, deltaWeight);
                        changed = true;
                    }
                    
                    currentEvent.Use();
                }
                else if (currentEvent.type == EventType.MouseUp)
                {
                    isDragging = false;
                    draggingSeparatorIndex = -1;
                    draggingControlId = -1;
                    currentEvent.Use();
                }
            }

            return changed;
        }

        // searchStart から方向 (searchLeft) に向かって Unlocked なボーンを探す
        private static int FindUnlockBoneIndex(List<BoneWeightInfo> weights, int searchStart, bool searchLeft)
        {
            if (searchLeft)
            {
                for (int i = searchStart; i >= 0; i--)
                {
                    if (!weights[i].isLocked) return i;
                }
            }
            else
            {
                for (int i = searchStart; i < weights.Count; i++)
                {
                    if (!weights[i].isLocked) return i;
                }
            }
            return -1;
        }

        private static void AdjustSmartWeights(List<BoneWeightInfo> weights, int leftIndex, int rightIndex, float delta)
        {
            var left = weights[leftIndex];
            var right = weights[rightIndex];

            float leftWeight = left.weight + delta;
            float rightWeight = right.weight - delta;

            if (leftWeight < 0)
            {
                float overflow = -leftWeight;
                leftWeight = 0;
                rightWeight -= overflow; 
                // rightWeight may increase, limit? No, usually total is 1.0, so increase is fine.
                // But wait, if rightWeight exceeds total available?
                // Logic: Left + Right = Const. 
            }
            else if (rightWeight < 0)
            {
                float overflow = -rightWeight;
                rightWeight = 0;
                leftWeight -= overflow;
            }

            left.weight = leftWeight;
            right.weight = rightWeight;
        }

        private static void AdjustWeights(List<BoneWeightInfo> weights, int separatorIndex, float delta)
        {
           // Legacy method kept if needed or can be removed.
           // Replacing usage above with AdjustSmartWeights
        }

        private static void NormalizeWeights(List<BoneWeightInfo> weights)
        {
            float total = weights.Sum(w => w.weight);
            if (total <= 0) return; // 全部0ならどうしようもない

            if (Mathf.Abs(total - 1.0f) > 0.001f)
            {
                foreach (var w in weights)
                {
                    w.weight /= total;
                }
            }
        }
    }
}

