#if UNITY_EDITOR && SHAPECHANGE_VRCSDK_AVATARS
using UnityEditor;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace ShapeChange
{
    public sealed class ShapeChangeBuildFileHandles : IVRCSDKPreprocessAvatarCallback, IVRCSDKPostprocessAvatarCallback
    {
        // shortcut: lilToon 2.4.1 runs at 100; recheck this boundary when the dependency changes.
        public int callbackOrder => 99;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            AssetDatabase.ReleaseCachedFileHandles();
            return true;
        }

        public void OnPostprocessAvatar()
        {
            AssetDatabase.ReleaseCachedFileHandles();
        }
    }
}
#endif
