using System;

namespace VertexWeightTool
{
    [Serializable]
    public class BoneWeightInfo
    {
        public int boneIndex;
        public string boneName;
        public float weight;
        public bool isLocked;

        public BoneWeightInfo(int index, string name, float w)
        {
            boneIndex = index;
            boneName = name;
            weight = w;
            isLocked = false;
        }
    }
}
