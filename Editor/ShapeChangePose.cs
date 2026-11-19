using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace ShapeChange
{
    // Editor-only rig posing. Export ordinary clips; no custom runtime component
    // or shader-generated elbow/knee bending is required on the VRChat avatar.
    public static class ShapeChangePose
    {
        static readonly HumanBodyBones[] Joints={HumanBodyBones.LeftUpperArm,HumanBodyBones.RightUpperArm,HumanBodyBones.LeftUpperLeg,HumanBodyBones.RightUpperLeg};
        static readonly string[] JointProperties={"_SCArmL","_SCArmR","_SCLegL","_SCLegR"};
        public static readonly HumanBodyBones[] PoseBones={HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand,HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand,HumanBodyBones.LeftUpperLeg,HumanBodyBones.LeftLowerLeg,HumanBodyBones.LeftFoot,HumanBodyBones.RightUpperLeg,HumanBodyBones.RightLowerLeg,HumanBodyBones.RightFoot};

        public static void Calibrate(Animator source,GameObject root)
        {
            var mats=ShapeChangeSetup.Materials(root);var frame=mats[0].GetVector("_SCFrame");
            float front=mats.First(m=>m.GetFloat("_SCProxy")<0.5f && m.GetFloat("_SCAccessory")<0.5f).GetFloat("_SCHeadFront"),panelBottom=mats[0].GetFloat("_SCBodyBase");
            foreach(var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if(!r.sharedMaterials.Any(m=>m && m.HasProperty("_SCReady") && m.GetFloat("_SCAccessory")<0.5f && m.GetFloat("_SCProxy")<0.5f && m.GetFloat("_SCAppendage")<0.5f))continue;
                var a=new System.Collections.Generic.List<Vector2>();var b=new System.Collections.Generic.List<Vector2>();
                r.sharedMesh.GetUVs(4,a);r.sharedMesh.GetUVs(5,b);
                var limbs=new System.Collections.Generic.List<Vector2>();r.sharedMesh.GetUVs(6,limbs);
                if(ShapeChangeSetup.VisibleForMeasurement(r,root.transform))
                    for(int i=0;i<a.Count;i++)if(b[i].y<0.1f && limbs[i].x<0.1f && limbs[i].y<0.1f)
                        panelBottom=Mathf.Min(panelBottom,a[i].y);
            }
            foreach(var m in mats)
            {
                for(int i=0;i<Joints.Length;i++)m.SetVector(JointProperties[i],(source.transform.InverseTransformPoint(source.GetBoneTransform(Joints[i]).position)-(Vector3)frame)/frame.w);
                m.SetFloat("_SCRigReady",1);m.SetFloat("_SCHeadFront",front);
                m.SetFloat("_SCPanelBottom",Mathf.Max(0,panelBottom));
                m.SetFloat("_SCUpright",1);m.SetFloat("_SCAccessoryAutoScale",1);m.SetFloat("_SCAccessoryScale",1);
                m.SetFloat("_SCSmoothEnvelope",1);
                EditorUtility.SetDirty(m);
            }
        }

        // Keep this small CPU counterpart in sync with SCHalfSize/SCLimbTarget.
        // It locates the IK targets, while the GPU moves the already posed limb.
        internal static Vector3 HalfSize(Material m)
        {
            int mode=Mathf.RoundToInt(m.GetFloat("_SCMode"));float size=Mathf.Max(m.GetFloat("_SCSize"),0.05f);
            if(mode==1)
            {
                var aspect=m.GetVector("_SCBoxAspect");
                return Vector3.Scale(new Vector3(0.24f,0.11f,0.43f),
                    new Vector3(Mathf.Max(aspect.x,0.1f),Mathf.Max(aspect.y,0.1f),Mathf.Max(aspect.z,0.1f)))*size;
            }
            if(mode==2)return Vector3.one*0.28f*size;
            if(mode==3)
            {
                float inflation=Mathf.Max(m.GetFloat("_SCInflation"),0.1f);
                return new Vector3(0.5f*inflation,0.5f,0.5f*inflation)*size;
            }
            if(mode==5)return Vector3.one*0.25f*size;
            return Vector3.one*0.35f*size;
        }

        public static void Apply(Animator source,GameObject root,bool seated)
        {
            if(!source || source.gameObject==root)throw new InvalidOperationException("元アバターと複製を指定してください。");
            var rig=root.GetComponent<Animator>();var mats=ShapeChangeSetup.Materials(root);var mat=mats[0];
            int mode=Mathf.RoundToInt(mat.GetFloat("_SCMode"));
            if(seated && mode>5)throw new InvalidOperationException("鎮座ポーズは箱化・球体化（形状0〜5）で使用してください。");
            foreach(var id in PoseBones)
            {
                var bone=rig.GetBoneTransform(id);var original=source.GetBoneTransform(id);
                if(!bone || !original)throw new InvalidOperationException("ポーズ用の必須ボーンがありません: "+id);
                bone.localRotation=original.localRotation;
            }
            foreach(var m in mats){m.SetFloat("_SCSeated",seated?1:0);EditorUtility.SetDirty(m);}
            if(!seated)return;
            var frame=mat.GetVector("_SCFrame");var half=HalfSize(mat);
            float referenceHeight=half.y*2;
            bool uprightHeaded=mat.GetFloat("_SCUpright")>0.5f && (mode==2 || mode==5);
            if(uprightHeaded)half*=0.6f;
            var measure=mat.GetVector("_SCMeasure");var head=mat.GetVector("_SCHead");
            float area=4*half.x*half.y*((mode==3 || mode==4 || mode==5)?Mathf.PI/4:1);
            float coverage=mat.GetFloat("_SCFaceCoverage");
            float headScale=Mathf.Sqrt(coverage*area/(Mathf.Max(head.z*2,measure.y*0.65f)*measure.y*Mathf.PI/4));
            var faceSize=mat.GetVector("_SCFaceSize");if(faceSize.x>0.01f && faceSize.y>0.01f)headScale=Mathf.Sqrt(coverage*area/(faceSize.x*faceSize.y*Mathf.PI/4));
            float headHeight=faceSize.w>faceSize.z+0.03f?faceSize.w-faceSize.z:measure.y;
            if(mode<=5)headScale=referenceHeight/(headHeight*2.5f);
            if(mode==3)headScale=Mathf.Max(mat.GetFloat("_SCSize"),0.05f);
            float bodyTop=uprightHeaded?half.y*2:Mathf.Max(half.y*2-headHeight*headScale*0.82f,half.y*0.5f);
            float size=Mathf.Clamp(Mathf.Sqrt(half.x*half.y/Mathf.Max(measure.z*(measure.x-mat.GetFloat("_SCPanelBottom"))*0.5f,0.001f)),1,2);
            float armMorph=Mathf.Clamp01(mat.GetFloat("_SCArmMorph")),legMorph=Mathf.Clamp01(mat.GetFloat("_SCLegMorph"));
            float armScale=Mathf.Lerp(1,size,4*armMorph*(1-armMorph)),legScale=Mathf.Lerp(1,size,4*legMorph*(1-legMorph));
            float armWidth=armScale,legWidth=legScale;
            if(mode==3)
            {
                float shortScale=Mathf.Max(mat.GetFloat("_SCSize"),0.05f)*0.25f;
                armScale=Mathf.Lerp(1,shortScale,mat.GetFloat("_SCArms")<0.5f?armMorph:1);
                legScale=Mathf.Lerp(1,shortScale,mat.GetFloat("_SCLegs")<0.5f?legMorph:1);
                float wideScale=Mathf.Max(mat.GetFloat("_SCSize"),0.05f)*0.7f;
                armWidth=Mathf.Lerp(1,wideScale,mat.GetFloat("_SCArms")<0.5f?armMorph:1);
                legWidth=Mathf.Lerp(1,wideScale,mat.GetFloat("_SCLegs")<0.5f?legMorph:1);
            }
            for(int side=0;side<2;side++)
            {
                bool left=side==0;float sign=Mathf.Sign(mat.GetVector(left?"_SCArmL":"_SCArmR").x);
                var upper=rig.GetBoneTransform(left?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm);
                var lower=rig.GetBoneTransform(left?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm);
                var hand=rig.GetBoneTransform(left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand);
                Vector3 shoulder=new Vector3(sign*half.x*0.92f,mat.GetFloat("_SCUpright")>0.5f?bodyTop*0.9f:half.y*1.38f,half.z*0.8f);
                Vector3 palm=new Vector3(sign*half.x*0.32f,bodyTop*0.6f,half.z*1.12f);
                if(mode==3)
                {
                    shoulder=new Vector3(sign*half.x*0.91f,half.y*1.45f,half.z*0.43f);
                    palm=new Vector3(sign*half.x*0.32f,half.y*0.9f,half.z*0.94f);
                    float armLength=(Vector3.Distance(upper.position,lower.position)+Vector3.Distance(lower.position,hand.position))/root.transform.lossyScale.y/frame.w;
                    var outward=(shoulder-new Vector3(0,half.y,0)).normalized;
                    // A shortened hand cannot reach the old belly target; keep the stub outside the sphere.
                    palm=Vector3.Lerp(palm,shoulder+outward*armLength*armScale*0.9f,mat.GetFloat("_SCArms")<0.5f?armMorph:1);
                }
                Vector3 armDelta=root.transform.TransformVector((palm-shoulder)*frame.w/armScale);
                if(mode==3)armDelta=root.transform.TransformVector(Vector3.Scale((palm-shoulder)*frame.w,new Vector3(1/armScale,1/armWidth,1/armWidth)));
                SolveTwoBone(upper,lower,hand,upper.position+armDelta,root.transform.TransformDirection(new Vector3(sign,0,1)));
                // Keep the wrist's local rotation so fingers continue the forearm
                // instead of snapping back toward the original T-pose direction.
                upper=rig.GetBoneTransform(left?HumanBodyBones.LeftUpperLeg:HumanBodyBones.RightUpperLeg);
                lower=rig.GetBoneTransform(left?HumanBodyBones.LeftLowerLeg:HumanBodyBones.RightLowerLeg);
                var foot=rig.GetBoneTransform(left?HumanBodyBones.LeftFoot:HumanBodyBones.RightFoot);
                float length=(Vector3.Distance(upper.position,lower.position)+Vector3.Distance(lower.position,foot.position))/root.transform.lossyScale.y/frame.w;
                Vector3 legDelta=new Vector3(sign*0.02f/legScale,(0.055f-half.y*0.38f)/legScale,length*0.92f);
                if(mode==3)legDelta=new Vector3(sign*0.02f/legWidth,legDelta.y,length*0.92f*legScale/legWidth);
                SolveTwoBone(upper,lower,foot,upper.position+root.transform.TransformVector(legDelta*frame.w),root.transform.up);
                foot.rotation=root.transform.rotation*Quaternion.Inverse(source.transform.rotation)*source.GetBoneTransform(left?HumanBodyBones.LeftFoot:HumanBodyBones.RightFoot).rotation;
            }
            SceneView.RepaintAll();
        }

        static void SolveTwoBone(Transform upper,Transform lower,Transform end,Vector3 target,Vector3 pole)
        {
            float a=Vector3.Distance(upper.position,lower.position),b=Vector3.Distance(lower.position,end.position);
            Vector3 direction=(target-upper.position).normalized;
            float distance=Mathf.Clamp(Vector3.Distance(upper.position,target),Mathf.Abs(a-b)+0.0001f,(a+b)*0.98f);
            float along=(a*a+distance*distance-b*b)/(2*distance);
            Vector3 bend=Vector3.ProjectOnPlane(pole,direction).normalized;
            Vector3 elbow=upper.position+direction*along+bend*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
            upper.rotation=Quaternion.FromToRotation(lower.position-upper.position,elbow-upper.position)*upper.rotation;
            Vector3 goal=upper.position+direction*distance;
            lower.rotation=Quaternion.FromToRotation(end.position-lower.position,goal-lower.position)*lower.rotation;
        }

        internal static Action CaptureMergedPose(Animator source,GameObject root)
        {
            if(!source || source.gameObject==root)throw new InvalidOperationException("元アバターと複製を指定してください。");
            var aliases=ShapeChangeSetup.MergeArmatureAliases(root);
            var offsets=new Dictionary<Transform,(Transform target,Vector3 position,Quaternion rotation)>();
            foreach(var pair in aliases)
            {
                var bone=pair.Key;var target=pair.Value;
                if(!target.IsChildOf(root.transform))continue;
                var originalBone=source.transform.Find(AnimationUtility.CalculateTransformPath(bone,root.transform));
                var originalTarget=source.transform.Find(AnimationUtility.CalculateTransformPath(target,root.transform));
                if(!originalBone || !originalTarget){originalBone=bone;originalTarget=target;}
                offsets.Add(bone,(target,originalTarget.InverseTransformPoint(originalBone.position),Quaternion.Inverse(originalTarget.rotation)*originalBone.rotation));
            }

            return ()=>
            {
                var applied=new HashSet<Transform>();
                var visiting=new HashSet<Transform>();
                void Follow(Transform bone)
                {
                    if(!bone || bone==root.transform || applied.Contains(bone))return;
                    if(!visiting.Add(bone))throw new InvalidOperationException("MAのボーン対応が循環しています: "+bone.name);
                    Follow(bone.parent);
                    if(offsets.TryGetValue(bone,out var offset))
                    {
                        Follow(offset.target);
                        bone.SetPositionAndRotation(offset.target.TransformPoint(offset.position),offset.target.rotation*offset.rotation);
                    }
                    visiting.Remove(bone);
                    applied.Add(bone);
                }
                foreach(var bone in offsets.Keys)Follow(bone);
            };
        }

        public static void ApplyPlane(Animator source,GameObject root)
        {
            var followGarments=CaptureMergedPose(source,root);
            ApplyPlaneRig(source,root);
            followGarments();
        }

        static void ApplyPlaneRig(Animator source,GameObject root)
        {
            if(!source || source.gameObject==root)throw new InvalidOperationException("元アバターと複製を指定してください。");
            var rig=root.GetComponent<Animator>();
            // Reset bones only; capture clones can still reference persistent animation materials.
            foreach(var bone in PoseBones)rig.GetBoneTransform(bone).localRotation=source.GetBoneTransform(bone).localRotation;
            foreach(bool left in new[]{true,false})
            {
                var upper=rig.GetBoneTransform(left?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm);
                var lower=rig.GetBoneTransform(left?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm);
                var hand=rig.GetBoneTransform(left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand);
                float sign=Mathf.Sign(root.transform.InverseTransformPoint(upper.position).x),length=Vector3.Distance(upper.position,lower.position)+Vector3.Distance(lower.position,hand.position);
                var down=root.transform.TransformDirection(new Vector3(sign*0.1f,-1,0.035f).normalized);
                SolveTwoBone(upper,lower,hand,upper.position+down*length*0.97f,root.transform.forward);
            }
        }

        public static void ExportPlaneClip(Animator source,GameObject root,string folder)
        {
            var rig=root.GetComponent<Animator>();var bones=PoseBones.Select(rig.GetBoneTransform).ToArray();
            // A quaternion setter renormalizes some imported bones; restore their serialized values exactly.
            var states=bones.Select(EditorJsonUtility.ToJson).ToArray();
            try
            {
                ApplyPlaneRig(source,root);
                using(var handler=new HumanPoseHandler(rig.avatar,root.transform))
                {
                    var pose=new HumanPose();handler.GetHumanPose(ref pose);
                    SavePoseClips(root,pose,folder+"/Pose_Plane.anim",false,false);
                }
            }
            finally{for(int i=0;i<bones.Length;i++)EditorJsonUtility.FromJsonOverwrite(states[i],bones[i]);}
        }

        static void SavePoseClips(GameObject root,HumanPose humanPose,string path,bool seated,bool shortLimbs)
        {
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);bool create=!clip;
            if(create)clip=new AnimationClip{name=Path.GetFileNameWithoutExtension(path)};
            clip.ClearCurves();
            foreach(var bone in PoseBones)for(int axis=0;axis<3;axis++)
            {
                int muscle=HumanTrait.MuscleFromBone((int)bone,axis);if(muscle<0)continue;
                AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve("",typeof(Animator),HumanTrait.MuscleName[muscle]),AnimationCurve.Constant(0,1,humanPose.muscles[muscle]));
            }
            if(create)AssetDatabase.CreateAsset(clip,path);else EditorUtility.SetDirty(clip);
            if(!clip.humanMotion)throw new InvalidOperationException("Humanoidポーズの書き出しに失敗: "+path);
            string fxPath=path.Replace(".anim","_FX.anim");var fx=AssetDatabase.LoadAssetAtPath<AnimationClip>(fxPath);bool newFx=!fx;
            if(newFx)fx=new AnimationClip{name=Path.GetFileNameWithoutExtension(fxPath)};
            fx.ClearCurves();var mats=ShapeChangeSetup.Materials(root);
            foreach(var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))if(r.sharedMaterials.Any(m=>m && m.HasProperty("_SCReady")))
            {
                string binding=AnimationUtility.CalculateTransformPath(r.transform,root.transform);
                foreach(string key in new[]{"_SCSeated","_SCArms","_SCLegs"})
                    AnimationUtility.SetEditorCurve(fx,EditorCurveBinding.FloatCurve(binding,typeof(SkinnedMeshRenderer),"material."+key),AnimationCurve.Constant(0,1,key=="_SCSeated"?seated?1:0:shortLimbs?1:0));
                foreach(string key in new[]{"_SCArmMorph","_SCLegMorph"})
                    AnimationUtility.SetEditorCurve(fx,EditorCurveBinding.FloatCurve(binding,typeof(SkinnedMeshRenderer),"material."+key),AnimationCurve.Constant(0,1,mats[0].GetFloat(key)));
            }
            if(newFx)AssetDatabase.CreateAsset(fx,fxPath);else EditorUtility.SetDirty(fx);
        }

        public static void ExportClips(Animator source,GameObject root,string folder)
        {
            var rig=root.GetComponent<Animator>();var bones=PoseBones.Select(rig.GetBoneTransform).ToArray();
            var rotations=bones.Select(b=>b.localRotation).ToArray();var mats=ShapeChangeSetup.Materials(root);
            var old=mats.Select(m=>new Vector4(m.GetFloat("_SCMode"),m.GetFloat("_SCSeated"),m.GetFloat("_SCArms"),m.GetFloat("_SCLegs"))).ToArray();
            using(var handler=new HumanPoseHandler(rig.avatar,root.transform))
            try
            {
                for(int pose=-1;pose<6;pose++)
                {
                    for(int i=0;i<mats.Length;i++){mats[i].SetFloat("_SCMode",Mathf.Max(0,pose));mats[i].SetFloat("_SCArms",pose==3?1:old[i].z);mats[i].SetFloat("_SCLegs",pose==3?1:old[i].w);}Apply(source,root,pose>=0);
                    string path=folder+(pose<0?"/Pose_Original.anim":"/Pose_Seated_"+pose.ToString("D2")+".anim");
                    var humanPose=new HumanPose();handler.GetHumanPose(ref humanPose);
                    SavePoseClips(root,humanPose,path,pose>=0,pose==3);
                }
            }
            finally
            {
                for(int i=0;i<bones.Length;i++)bones[i].localRotation=rotations[i];
                for(int i=0;i<mats.Length;i++){mats[i].SetFloat("_SCMode",old[i].x);mats[i].SetFloat("_SCSeated",old[i].y);mats[i].SetFloat("_SCArms",old[i].z);mats[i].SetFloat("_SCLegs",old[i].w);}
            }
            // Two endpoints per control can be used directly in separate FX
            // blend trees; each clip only owns its corresponding limb settings.
            foreach(bool arm in new[]{true,false})foreach(int endpoint in new[]{0,1})
            {
                string path=folder+"/Morph_"+(arm?"Arms":"Legs")+"_"+endpoint+".anim";
                var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);bool create=!clip;
                if(create)clip=new AnimationClip{name=Path.GetFileNameWithoutExtension(path)};
                clip.ClearCurves();
                foreach(var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))if(r.sharedMaterials.Any(m=>m && m.HasProperty("_SCReady")))
                {
                    string binding=AnimationUtility.CalculateTransformPath(r.transform,root.transform);
                    AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(binding,typeof(SkinnedMeshRenderer),"material."+(arm?"_SCArms":"_SCLegs")),AnimationCurve.Constant(0,1,0));
                    AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(binding,typeof(SkinnedMeshRenderer),"material."+(arm?"_SCArmMorph":"_SCLegMorph")),AnimationCurve.Constant(0,1,endpoint));
                }
                if(create)AssetDatabase.CreateAsset(clip,path);else EditorUtility.SetDirty(clip);
            }
            ExportPlaneClip(source,root,folder);
        }

        [MenuItem("Tools/ShapeChange/Upgrade Posture and Scale")]
        public static void UpgradePreview()
        {
            var root=Object.FindObjectsOfType<Animator>().First(a=>a.name.EndsWith("_ShapeChange"));Upgrade(ShapeChangeSetup.FindAvatar(),root.gameObject);
        }

        public static void Upgrade(Animator source,GameObject root)
        {
            var template=ShapeChangeSetup.Materials(root).First(m=>m.GetFloat("_SCProxy")<0.5f && m.GetFloat("_SCAccessory")<0.5f);
            string folder=Path.GetDirectoryName(AssetDatabase.GetAssetPath(template)).Replace('\\','/');
            if(!folder.StartsWith(ShapeChangeSetup.Folder+"/Generated/",StringComparison.Ordinal))throw new InvalidOperationException("Generated以下の複製を指定してください。");
            ShapeChangeSetup.CalibrateHead(root);Calibrate(source,root);ShapeChangeSetup.UpdateAccessories(source,root,false,true);
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach(var asset in ShapeChangeSetup.Materials(root).Cast<Object>().Concat(root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r=>(Object)r.sharedMesh)).Distinct())
                {
                    string path=AssetDatabase.GetAssetPath(asset);if(!path.StartsWith(folder+"/",StringComparison.Ordinal))continue;
                    asset.name=Path.GetFileNameWithoutExtension(path);EditorUtility.SetDirty(asset);
                }
                ExportClips(source,root,folder);AssetDatabase.SaveAssets();
            }
            finally{AssetDatabase.StopAssetEditing();}
            if(root.scene.path.StartsWith(ShapeChangeSetup.Folder+"/Generated/",StringComparison.Ordinal))EditorSceneManager.SaveScene(root.scene);
            Debug.Log("[ShapeChange] 頭頂〜顎下・襟・装着先倍率・8組の姿勢Clip・腕脚4本のモーフClipを更新: "+folder);
        }
    }
}
