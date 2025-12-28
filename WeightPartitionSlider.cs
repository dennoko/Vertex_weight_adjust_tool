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

                    // i番目とi+1番目のウェイトを調整
                    // セパレータiは、bone[i]とbone[i+1]の間にある
                    int indexVerify = draggingSeparatorIndex;
                    if (indexVerify >= 0 && indexVerify < boneWeights.Count - 1)
                    {
                        AdjustWeights(boneWeights, indexVerify, deltaWeight);
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

        private static void AdjustWeights(List<BoneWeightInfo> weights, int separatorIndex, float delta)
        {
            // separatorIndex は index と index+1 の境界
            // 右に動かす(delta > 0) -> Left(index)が増える、Right(index+1)が減る
            
            var left = weights[separatorIndex];
            var right = weights[separatorIndex + 1];

            float leftWeight = left.weight + delta;
            float rightWeight = right.weight - delta;

            // 境界チェック (0 ~ 1) かつ、隣の領域を侵食しすぎないように
            // ここでは簡易的に 0を下回らないようにする
            if (leftWeight < 0)
            {
                // 左が0になるまで戻す
                float overflow = -leftWeight;
                leftWeight = 0;
                rightWeight -= overflow; // rightは増える方向には制限なし（合計1なので他が減るだけだが、ここでは2者間移動）
                                         // ただし2者間移動なので left+right=const を保つ必要がある
                rightWeight = left.weight + right.weight; // 全部右へ
            }
            else if (rightWeight < 0)
            {
                // 右が0になるまで
                float overflow = -rightWeight;
                rightWeight = 0;
                leftWeight = left.weight + right.weight; // 全部左へ
            }

            left.weight = leftWeight;
            right.weight = rightWeight;
            
            // クラスなので参照元の値が変わるが、Listの中身がstruct等だと反映されないので注意
            // ここではBoneWeightInfoがclassであることを前提とする
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

    // データ保持用クラス
    public class BoneWeightInfo
    {
        public int boneIndex;
        public string boneName;
        public float weight;

        public BoneWeightInfo(int index, string name, float w)
        {
            boneIndex = index;
            boneName = name;
            weight = w;
        }
    }
}
