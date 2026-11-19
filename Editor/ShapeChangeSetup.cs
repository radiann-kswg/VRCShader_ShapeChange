using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ShapeChange
{
    public class ShapeChangeSetup : EditorWindow
    {
        public const string Folder = "Assets/ShapeChangeShader/Shaders";
        public const string ShaderFolder = "Packages/net.numbertales-radiann.shapechange";
        public static readonly string[] Modes = { "立方体", "直方体", "頭付き立方体", "膨体", "真球", "頭付き2.5頭身球体", "円柱・顔上面", "円柱・顔側面", "コイン", "人型平面・水平回転", "人型平面・奥行圧縮", "人型平面・細幅", "人型平面・板幅", "縦平面化・頭上から足元へ圧縮", "長方形の板・紙（表／裏）", "長方形の反物（表／裏）" };
        [SerializeField] Animator avatar;
        [SerializeField] List<Renderer> excluded = new List<Renderer>();
        Vector2 scroll;
        bool overwriteUV;
        bool closedSurface=true;
        bool preserveSurface=true;
        float faceCoverage=0.4f, surfaceFollow=0.12f;
        bool upright=true;
        int rigPose;
        [SerializeField] GameObject preview;
        string setupError, previewError;
        int mode, arms = 1, legs = 1;
        float armMorph=0.35f,legMorph=0.35f;
        float faceStart=0.44f, bodyBase=0.45f;

        [MenuItem("Tools/ShapeChange/Setup Avatar")]
        public static void Open()
        {
            var window = GetWindow<ShapeChangeSetup>("ShapeChange");
            window.minSize = new Vector2(440,500);
            var selected = SelectedAvatar();
            if (selected && selected!=window.avatar) window.SetAvatar(selected);
            else if (!IsSourceAvatar(window.avatar)) window.SetAvatar(FindAvatar());
            if (!window.preview)
            {
                var prepared=Object.FindObjectsOfType<Animator>().Where(a=>a.name.EndsWith("_ShapeChange",StringComparison.Ordinal));
                var previewRig=window.avatar
                    ? prepared.FirstOrDefault(a=>a.name==window.avatar.name+"_ShapeChange")
                    : prepared.FirstOrDefault(a=>a.gameObject.scene==SceneManager.GetActiveScene());
                window.preview=previewRig?previewRig.gameObject:null;
            }
        }

        static bool IsHumanoid(Animator value) => value && value.avatar && value.avatar.isValid && value.avatar.isHuman;
        static bool HasPreparedAncestor(Transform value)
        {
            for(var current=value;current;current=current.parent)
                if(current.name.EndsWith("_ShapeChange",StringComparison.Ordinal))return true;
            return false;
        }
        static bool IsSourceAvatar(Animator value) => IsHumanoid(value) && value.gameObject.scene.IsValid() && !HasPreparedAncestor(value.transform);
        static Animator SelectedAvatar() => Selection.activeGameObject
            ? Selection.activeGameObject.GetComponentsInParent<Animator>(true).LastOrDefault(IsSourceAvatar) : null;
        public static Animator FindAvatar() => SelectedAvatar() ?? Object.FindObjectsOfType<Animator>().FirstOrDefault(a=>
            IsSourceAvatar(a) && (!a.transform.parent || !a.transform.parent.GetComponentsInParent<Animator>(true).Any(IsSourceAvatar)));

        void SetAvatar(Animator value)
        {
            avatar = value;
            excluded.Clear();
            setupError = null;
            previewError = null;
            if (!avatar) return;
            foreach (var r in PreparationRenderers(avatar))
                if (IsAccessory(r.name)) excluded.Add(r);
        }

        public static bool IsAccessory(string name)
        {
            string n = name.ToLowerInvariant();
            return IsEarOrTail(name) || System.Text.RegularExpressions.Regex.IsMatch(n,@"(?:^|[_ -])(?:acc|wings?)$") || new[] { "accessor", "necklace", "hairpin", "wristband", "glasses", "earring", "アクセ", "眼鏡", "メガネ" }.Any(n.Contains);
        }

        public static bool IsEarOrTail(string name)=>System.Text.RegularExpressions.Regex.IsMatch(name,@"(?:^|[_ -])(?:ears?|tails?|mimi|sippo)$|^(?:BodyEars?|BodyTail|獣耳|尻尾)$",System.Text.RegularExpressions.RegexOptions.IgnoreCase|System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        static bool IsEar(string name)=>IsEarOrTail(name) && (name.IndexOf("ear",StringComparison.OrdinalIgnoreCase)>=0 || name.EndsWith("mimi",StringComparison.OrdinalIgnoreCase) || name=="獣耳");

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawPreparation();
            EditorGUILayout.Space();
            DrawPreview();
            EditorGUILayout.EndScrollView();
        }

        void DrawPreparation()
        {
            EditorGUILayout.LabelField("体格に合わせた形状変化", EditorStyles.boldLabel);
            var next = (Animator)EditorGUILayout.ObjectField("アバター（Humanoid）", avatar, typeof(Animator), true);
            if (next != avatar) SetAvatar(next);
            EditorGUILayout.HelpBox("Built-in Render PipelineのHumanoidをTポーズで準備します。元のアバターと素材を保持し、専用メッシュ・素材・Prefabを作成します。", MessageType.Info);
            if (!avatar) return;

            var renderers=PreparationRenderers(avatar);
            EditorGUILayout.LabelField("形状を変更するメッシュ（オフ＝形状保持・装着位置に追従）");
            EditorGUILayout.LabelField("Standard（Opaque）または対応するlilToonを使います。形状保持のメッシュにも対応素材が必要です。", EditorStyles.wordWrappedMiniLabel);
            foreach (var r in renderers)
            {
                var path=AnimationUtility.CalculateTransformPath(r.transform,avatar.transform);
                bool include=EditorGUILayout.ToggleLeft(path.Length>0?path:r.name,!excluded.Contains(r));
                if (include) excluded.Remove(r); else if (!excluded.Contains(r)) excluded.Add(r);
            }
            overwriteUV=EditorGUILayout.ToggleLeft("使用済みUV5〜UV8を複製メッシュ上で上書きする",overwriteUV);
            closedSurface=EditorGUILayout.ToggleLeft("閉じた補助メッシュで隙間を防ぐ（現在の外観を焼き込み）",closedSurface);
            var issue=PreparationIssue(avatar,renderers,excluded,overwriteUV);
            if (issue!=null) EditorGUILayout.HelpBox(issue,MessageType.Warning);
            using (new EditorGUI.DisabledScope(issue!=null || EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("複製してセットアップ",GUILayout.Height(32)))
                {
                    setupError=null;
                    try { preview=Build(avatar,excluded,overwriteUV,closedSurface); previewError=null; }
                    catch (Exception e) { Debug.LogException(e); setupError=e.Message; }
                }
            if (!string.IsNullOrEmpty(setupError)) EditorGUILayout.HelpBox(setupError,MessageType.Error);
        }

        void DrawPreview()
        {
            var target=(GameObject)EditorGUILayout.ObjectField("操作する複製",preview,typeof(GameObject),true);
            if (target!=preview) { preview=target; previewError=null; }
            if (!preview) return;
            var materials=Materials(preview);
            if (materials.Length==0)
            {
                EditorGUILayout.HelpBox("セットアップ済みのアバター複製またはPrefabを指定してください。",MessageType.Warning);
                return;
            }
            var settings=materials[0];
            var rig=preview.GetComponent<Animator>();
            bool poseReady=IsHumanoid(avatar) && IsHumanoid(rig) && avatar!=rig && avatar.avatar==rig.avatar && !EditorUtility.IsPersistent(preview);
            bool hasShell=materials.Any(m=>m.GetFloat("_SCProxy")>0.5f);
            if (!poseReady)
                EditorGUILayout.HelpBox("姿勢や焼き込みを変更する場合は、対応する元アバターとシーン内の複製を指定してください。",MessageType.Info);
            if (Event.current.type==EventType.Layout)
            {
                mode=Mathf.Clamp(Mathf.RoundToInt(settings.GetFloat("_SCMode")),0,Modes.Length-1);
                arms=Mathf.Clamp(Mathf.RoundToInt(settings.GetFloat("_SCArms")),0,2);
                legs=Mathf.Clamp(Mathf.RoundToInt(settings.GetFloat("_SCLegs")),0,2);
                armMorph=settings.GetFloat("_SCArmMorph");legMorph=settings.GetFloat("_SCLegMorph");
                preserveSurface=settings.GetFloat("_SCPreserveSurface")>0.5f;
                faceCoverage=settings.GetFloat("_SCFaceCoverage");surfaceFollow=settings.GetFloat("_SCSurfaceFollow");
                faceStart=settings.GetFloat("_SCFaceStart");bodyBase=settings.GetFloat("_SCBodyBase");
                upright=settings.GetFloat("_SCUpright")>0.5f;rigPose=Mathf.RoundToInt(settings.GetFloat("_SCSeated"));
            }
            EditorGUI.BeginChangeCheck();
            bool shapeOn=EditorGUILayout.ToggleLeft("変形（On / Off）",settings.GetFloat("_SCEnabled")>0.5f && settings.GetFloat("_SCAmount")>=0.5f);
            int previousMode=mode;
            mode=EditorGUILayout.Popup("形状",mode,Modes);
            float size=EditorGUILayout.Slider("形状の大きさ",settings.GetFloat("_SCSize"),0.2f,2);
            float thickness=settings.GetFloat("_SCThickness"),layer=settings.GetFloat("_SCLayer");
            if (mode>=8) thickness=EditorGUILayout.Slider("厚み / 身長比",thickness,mode==8?0.002f:0.004f,0.2f);
            if (mode!=13) layer=EditorGUILayout.Slider("衣装の重なり保持",layer,0.002f,0.15f);
            Vector4 boxAspect=settings.GetVector("_SCBoxAspect");
            float inflation=settings.GetFloat("_SCInflation"),spread=settings.GetFloat("_SCPressSpread");
            float panelWidth=settings.GetFloat("_SCPanelWidth"),bend=settings.GetFloat("_SCClothBend");
            if (mode==1) boxAspect=EditorGUILayout.Vector3Field("直方体の幅・高さ・奥行倍率",boxAspect);
            if (mode==3) inflation=EditorGUILayout.Slider("横・奥行の膨らみ（1＝球比率）",inflation,0.5f,2.5f);
            if (mode==13) spread=EditorGUILayout.Slider("圧縮時の横への広がり",spread,0,2);
            if (mode>=14) panelWidth=EditorGUILayout.Slider("長方形の幅 / 身長比",panelWidth,0.1f,1.5f);
            if (mode==15) bend=EditorGUILayout.Slider("反物のたわみ",bend,0,0.2f);
            if (mode>=14 && (!hasShell || settings.GetFloat("_SCUseProxy")<0.5f))
                EditorGUILayout.HelpBox("閉じた長方形には、セットアップ時の補助メッシュと表裏の焼き込みが必要です。",MessageType.Warning);
            if (mode<9)
            {
                arms = EditorGUILayout.Popup("腕", arms, new[] { "残す", mode==3?"短い四肢を残す":"胴体へ収納", "非表示" });
                legs = EditorGUILayout.Popup("脚", legs, new[] { "残す", mode==3?"短い四肢を残す":"胴体へ収納", "非表示" });
                if (arms==0) armMorph=EditorGUILayout.Slider("腕の変形・収納",armMorph,0,1);
                if (legs==0) legMorph=EditorGUILayout.Slider("脚の変形・収納",legMorph,0,1);
            }
            else if (mode<13)
            {
                arms=EditorGUILayout.ToggleLeft("腕を非表示",arms==2)?2:Mathf.Min(arms,1);
                legs=EditorGUILayout.ToggleLeft("脚を非表示",legs==2)?2:Mathf.Min(legs,1);
            }
            int nextPose=0;
            if (mode<=5)
                using (new EditorGUI.DisabledScope(!poseReady))
                    nextPose=EditorGUILayout.Popup("四肢のポーズ",rigPose,new[]{"元のアーマチュア姿勢",mode==3?"鎮座（短い腕を外へ・脚を前へ）":"鎮座（腕を腹へ・脚を前へ）"});
            bool smoothEnvelope=settings.GetFloat("_SCSmoothEnvelope")>0.5f;
            if (mode<9)
            {
                upright=EditorGUILayout.ToggleLeft("直立配置（オフ＝従来の前寄せ）",upright);
                smoothEnvelope=EditorGUILayout.ToggleLeft("凹凸を減らして外形へなじませる",smoothEnvelope);
                preserveSurface=EditorGUILayout.ToggleLeft("顔の縦横比・前後中央の形を保持",preserveSurface);
                if (preserveSurface)
                {
                    if (mode>5) faceCoverage=EditorGUILayout.Slider("顔の正面占有率（目安）",faceCoverage,0.05f,0.7f);
                    if (!smoothEnvelope) surfaceFollow=EditorGUILayout.Slider("胴体外周の形状への追従",surfaceFollow,0,1);
                }
                else faceStart=EditorGUILayout.Slider("顔の下端（焼き込み）",faceStart,0.3f,0.85f);
                bodyBase=EditorGUILayout.Slider("収納時の胴体下端",bodyBase,0,0.7f);
            }
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObjects(materials,"ShapeChange preview");
                if (poseReady && nextPose==1 && rigPose==0) { arms=mode==3?1:0;legs=arms; }
                foreach (var m in materials)
                {
                    m.SetFloat("_SCEnabled",1);
                    m.SetFloat("_SCMode",mode);m.SetFloat("_SCAmount",shapeOn?1:0);m.SetFloat("_SCArms",arms);m.SetFloat("_SCLegs",legs);
                    m.SetFloat("_SCSize",size);m.SetFloat("_SCThickness",thickness);m.SetFloat("_SCLayer",layer);
                    m.SetVector("_SCBoxAspect",boxAspect);m.SetFloat("_SCInflation",inflation);m.SetFloat("_SCPressSpread",spread);
                    m.SetFloat("_SCPanelWidth",panelWidth);m.SetFloat("_SCClothBend",bend);
                    m.SetFloat("_SCArmMorph",armMorph);m.SetFloat("_SCLegMorph",legMorph);
                    m.SetFloat("_SCSmoothEnvelope",smoothEnvelope?1:0);
                    m.SetFloat("_SCFaceStart",faceStart);m.SetFloat("_SCBodyBase",bodyBase);m.SetFloat("_SCPreserveSurface",preserveSurface?1:0);
                    m.SetFloat("_SCFaceCoverage",faceCoverage);m.SetFloat("_SCSurfaceFollow",surfaceFollow);m.SetFloat("_SCUpright",upright?1:0);
                    EditorUtility.SetDirty(m);
                }
                if (poseReady)
                {
                    if (mode>=9 && previousMode!=mode)
                        PreviewAction(()=>
                        {
                            ShapeChangePose.ApplyPlane(avatar,preview);
                            foreach (var m in materials) m.SetFloat("_SCSeated",0);
                        });
                    else if (mode<9 && (nextPose!=rigPose || nextPose==1 || previousMode>=9))
                        PreviewAction(()=>
                        {
                            var followGarments=ShapeChangePose.CaptureMergedPose(avatar,preview);
                            ShapeChangePose.Apply(avatar,preview,nextPose==1);
                            followGarments();
                        });
                }
                SceneView.RepaintAll();
            }
            using (new EditorGUI.DisabledScope(!poseReady))
            {
                if (mode>=9 && GUILayout.Button("平面向けに腕を体側へ下ろす"))
                    PreviewAction(()=>ShapeChangePose.ApplyPlane(avatar,preview));
                using (new EditorGUI.DisabledScope(!hasShell))
                    if (GUILayout.Button("現在の服・表情で外観を再焼き込み"))
                        PreviewAction(()=>ShapeChangeShell.Rebake(avatar,preview));
                if (GUILayout.Button("既存の複製へアクセサリー追従を適用"))
                    PreviewAction(()=>UpdateAccessories(avatar,preview,overwriteUV));
                if (GUILayout.Button("倍率・姿勢を更新してポーズClipを保存"))
                    PreviewAction(()=>ShapeChangePose.Upgrade(avatar,preview));
            }
            if (GUILayout.Button("現在の形状へイラスト調の陰影を適用"))
                PreviewAction(()=>ApplyIllustrationShading(preview,mode));
            var assetPath=AssetDatabase.GetAssetPath(settings);
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(assetPath)))
            {
                if (GUILayout.Button("追加形状を含むShape Clipを更新"))
                    PreviewAction(()=>CreateClips(preview,Path.GetDirectoryName(assetPath).Replace('\\','/')));
                if (GUILayout.Button("現在の形状の On / Off Controller を作成"))
                    PreviewAction(()=>
                    {
                        var path=ShapeChangeAnimations.CreateToggleController(preview,mode);
                        EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(path));
                        ShowNotification(new GUIContent("On / Off Controller を作成しました"));
                    });
            }
            if (!string.IsNullOrEmpty(previewError)) EditorGUILayout.HelpBox(previewError,MessageType.Error);
        }

        void PreviewAction(Action action)
        {
            previewError=null;
            try { action(); }
            catch (Exception e) { Debug.LogException(e); previewError=e.Message; }
        }

        public static Material[] Materials(GameObject root) => root.GetComponentsInChildren<Renderer>(true)
            .SelectMany(r => r.sharedMaterials).Concat(ShapeChangeAnimations.Materials(root)).Where(m => m && m.HasProperty("_SCReady") && m.GetFloat("_SCReady") > 0).Distinct().ToArray();

        public static void ApplyIllustrationShading(GameObject root,int shape)
        {
            var materials=Materials(root).Where(m=>m.HasProperty("_UseShadow")).ToArray();
            Undo.RecordObjects(materials,"ShapeChange illustration shading");
            foreach(var m in materials)
            {
                // Flat faces read as separate planes; round volumes need a wider light-to-shadow transition.
                m.SetFloat("_UseShadow",1);m.SetFloat("_ShadowStrength",0.7f);
                m.SetFloat("_ShadowBorder",0.5f);m.SetFloat("_ShadowBlur",shape>=3 && shape<=8?0.28f:0.07f);
                m.SetColor("_ShadowColor",new Color(0.68f,0.70f,0.78f,1));
                if(m.GetFloat("_SCProxy")>0.5f)m.SetFloat("_AsUnlit",0.2f);
                EditorUtility.SetDirty(m);
            }
            SceneView.RepaintAll();
        }

        public static void SetShape(GameObject root, int shape, float blend, int arms = 1, int legs = 1)
        {
            foreach (var m in Materials(root))
            {
                m.SetFloat("_SCMode",shape); m.SetFloat("_SCAmount",blend);
                m.SetFloat("_SCArms",arms); m.SetFloat("_SCLegs",legs);
            }
        }

        internal static bool VisibleForMeasurement(Renderer renderer,Transform root)
        {
            if(!renderer.enabled)return false;
            for(var current=renderer.transform;current && current!=root;current=current.parent)
                if(!current.gameObject.activeSelf)return false;
            return true;
        }

        public static void CalibrateHead(GameObject root)
        {
            var materials=Materials(root);
            if(materials.Length==0)return;
            float neck=materials[0].GetVector("_SCMeasure").x;
            var renderers=root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.sharedMesh && VisibleForMeasurement(r,root.transform)
                && r.sharedMaterials.Any(m=>m && m.HasProperty("_SCReady") && m.GetFloat("_SCReady")>0 && m.GetFloat("_SCProxy")<0.5f && m.GetFloat("_SCAccessory")<0.5f && m.GetFloat("_SCAppendage")<0.5f)).ToArray();
            // Use the avatar's explicit face reference without requiring the VRChat SDK in the shader package.
            var descriptor=root.GetComponents<Component>().FirstOrDefault(c=>c && c.GetType().FullName=="VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            var face=descriptor?descriptor.GetType().GetField("VisemeSkinnedMesh")?.GetValue(descriptor) as SkinnedMeshRenderer:null;
            if(face && renderers.Contains(face))renderers=new[]{face};
            var min=Vector3.one*float.PositiveInfinity;var max=Vector3.one*float.NegativeInfinity;
            var points=new List<Vector3>();
            foreach(var r in renderers)
            {
                var a=new List<Vector2>();var b=new List<Vector2>();r.sharedMesh.GetUVs(4,a);r.sharedMesh.GetUVs(5,b);
                if(a.Count!=r.sharedMesh.vertexCount || b.Count!=a.Count)throw new InvalidOperationException("頭部計測用の準備データがありません: "+r.name);
                for(int i=0;i<a.Count;i++)if(b[i].y>0.95f && a[i].y>=neck && a[i].y<=1.01f)
                {
                    var p=new Vector3(a[i].x,a[i].y,b[i].x);points.Add(p);min=Vector3.Min(min,p);max=Vector3.Max(max,p);
                }
            }
            if(float.IsInfinity(min.x))throw new InvalidOperationException("頭部のサイズを測定できません。");
            Vector3 center=(min+max)*0.5f;
            float chin=float.PositiveInfinity;
            foreach(var p in points)
                if(Mathf.Abs(p.x-center.x)<(max.x-min.x)*0.3f && p.z>center.z+(max.z-center.z)*0.45f)
                    chin=Mathf.Min(chin,p.y);
            if(float.IsInfinity(chin) || max.y-chin<0.03f)chin=neck;
            foreach(var m in materials)
            {
                m.SetVector("_SCHead",new Vector4(center.x,(chin+max.y)*0.5f,(max.x-min.x)*0.5f,center.z));
                Vector3 size=max-min;
                m.SetVector("_SCFaceSize",new Vector4(size.x,size.y,chin,max.y));
                m.SetFloat("_SCHeadFront",Mathf.Max(max.z-center.z,0.03f));EditorUtility.SetDirty(m);
            }
        }

        internal const string StandardShapeShader = "ShapeChange/Accessory Standard Opaque";

        static Shader ConvertedShader(Material material)
        {
            string n = material.shader ? material.shader.name : "";
            if(n=="" || n=="Hidden/InternalErrorShader")
                throw new InvalidOperationException("素材のシェーダーを読み込めません: " + material.name + "。アバターが使用するシェーダーを先に導入してください。");
            if(n=="Hidden/lilToonFurTwoPass")
                return Shader.Find("ShapeChange/[Optional] lilToonFurTwoPass") ?? throw new InvalidOperationException("TwoPassファー用の形状変化シェーダーがありません。lilToonを導入し、ShapeChangeを再インポートしてください。");
            if(n=="Standard" && material.GetFloat("_Mode")==0)
                return Shader.Find(StandardShapeShader) ?? throw new InvalidOperationException("Standard用の形状変化シェーダーがありません。");
            if (!n.Contains("lilToon") || new[] { "Fur", "Gem", "Refraction", "Lite", "Multi", "Tessellation" }.Any(n.Contains))
                throw new InvalidOperationException("未対応シェーダー: " + n + " / " + material.name + "。対象素材を通常版lilToonまたはStandardのOpaqueにしてください。");
            string type = n.Contains("Transparent") ? "Transparent" : n.Contains("Cutout") ? "Cutout" : "Opaque";
            string name = "ShapeChange/[Optional] lilToon" + type + (n.Contains("Outline") ? "Outline" : "");
            return Shader.Find(name) ?? throw new InvalidOperationException("シェーダーが見つかりません: " + name + "。lilToonを導入し、ShapeChangeを再インポートしてください。");
        }

        static Mesh SourceMesh(Renderer r) => r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
        static Vector3 BonePosition(Animator a, HumanBodyBones b) => a.transform.InverseTransformPoint(a.GetBoneTransform(b).position);
        static bool Under(Transform bone, Transform ancestor) => bone && ancestor && (bone == ancestor || bone.IsChildOf(ancestor));

        static Renderer[] PreparationRenderers(Animator source) => source
            ? source.GetComponentsInChildren<Renderer>(true).Where(r => r is SkinnedMeshRenderer || r is MeshRenderer).ToArray()
            : Array.Empty<Renderer>();

        static string PreparationIssue(Animator source, Renderer[] prepared, ICollection<Renderer> excluded, bool overwriteUV)
        {
            if (!IsHumanoid(source)) return "有効なHumanoid Animatorを指定してください。";
            if (HasPreparedAncestor(source.transform)) return "セットアップの入力には変形前のアバターを指定してください。";
            if (GraphicsSettings.currentRenderPipeline) return "Built-in Render Pipelineのプロジェクトでセットアップしてください。";
            var scale=source.transform.lossyScale;
            if(scale.x<=0 || scale.y<=0 || scale.z<=0 || Mathf.Abs(scale.x/scale.y-1)>0.001f || Mathf.Abs(scale.z/scale.y-1)>0.001f)
                return "アバタールートのScaleを正の均等倍率にしてください。";
            foreach (var bone in new[] { HumanBodyBones.Head, HumanBodyBones.Neck, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.LeftFoot })
                if (!source.GetBoneTransform(bone)) return "必須ボーンがありません。 " + bone;
            if (!prepared.Any(r=>!excluded.Contains(r))) return "変形するメッシュを一つ以上選択してください。";
            foreach (var r in prepared)
            {
                var mesh = SourceMesh(r);
                if (!mesh || mesh.vertexCount == 0) return "メッシュがありません。 " + r.name;
                for (int ch=4; ch<8; ch++)
                    if (!overwriteUV && mesh.HasVertexAttribute((VertexAttribute)((int)VertexAttribute.TexCoord0+ch)))
                        return r.name + " はUV" + (ch+1) + "を使用中です。上書きのチェックが必要です。";
                foreach (var m in r.sharedMaterials.Where(m => m))
                {
                    try { ConvertedShader(m); }
                    catch (InvalidOperationException e) { return $"{r.name} / {e.Message}"; }
                }
            }
            return null;
        }

        public static GameObject Build(Animator source, ICollection<Renderer> excluded, bool overwriteUV, bool closedSurface=true)
        {
            var prepared = PreparationRenderers(source);
            var issue = PreparationIssue(source,prepared,excluded,overwriteUV);
            if (issue!=null) throw new InvalidOperationException(issue);
            var root = source.transform;
            var included = prepared.Where(r=>!excluded.Contains(r)).ToArray();

            // Sample all renderers in a single avatar frame before changing rootBone.
            var positions = new Dictionary<Renderer,Vector3[]>();
            var normals = new Dictionary<Renderer,Vector3[]>();
            float floor = float.PositiveInfinity, top = float.NegativeInfinity, depth = 0.04f;
            var torsoDepths = new List<float>();
            var head = source.GetBoneTransform(HumanBodyBones.Head);
            float neckY = BonePosition(source, HumanBodyBones.Neck).y;
            float headY = BonePosition(source, HumanBodyBones.Head).y;
            float halfWidth = Vector3.Distance(BonePosition(source,HumanBodyBones.LeftUpperArm),BonePosition(source,HumanBodyBones.RightUpperArm))*0.5f;
            foreach (var r in prepared)
            {
                var baked = new Mesh();
                Mesh mesh = SourceMesh(r);
                if (r is SkinnedMeshRenderer skin) { skin.BakeMesh(baked); mesh = baked; }
                Matrix4x4 matrix = root.worldToLocalMatrix*r.transform.localToWorldMatrix;
                var p = mesh.vertices.Select(matrix.MultiplyPoint3x4).ToArray();
                var n = mesh.normals.Select(matrix.inverse.transpose.MultiplyVector).Select(v => v.normalized).ToArray();
                if (n.Length != p.Length) throw new InvalidOperationException("法線がありません: "+r.name);
                positions.Add(r,p); normals.Add(r,n);
                Object.DestroyImmediate(baked);
                if(excluded.Contains(r) || !VisibleForMeasurement(r,root))continue;
                foreach (var v in p)
                {
                    // Ignore tail/hair tips for stature; head height uses the head surface near its bone.
                    if (Mathf.Abs(v.x)<halfWidth*1.6f && Mathf.Abs(v.z)<halfWidth*2)
                    {
                        floor = Mathf.Min(floor,v.y);
                        if (v.y < headY + (headY-neckY)*4.1f) top = Mathf.Max(top,v.y);
                    }
                    if (v.y>neckY*0.5f && v.y<neckY*0.95f && Mathf.Abs(v.x)<halfWidth) torsoDepths.Add(Mathf.Abs(v.z));
                }
            }
            float height = top-floor;
            torsoDepths.Sort();
            if(torsoDepths.Count>0) depth=Mathf.Max(depth,torsoDepths[torsoDepths.Count*3/4]);
            if (height < 0.1f || float.IsInfinity(height)) throw new InvalidOperationException("身長を測定できません。");
            Vector4 frame = new Vector4(0,floor,0,height);
            Vector4 measure = new Vector4((neckY-floor)/height,(top-neckY)/height,halfWidth/height,depth/height);
            if (!AssetDatabase.IsValidFolder(Folder+"/Generated")) { Directory.CreateDirectory(Folder+"/Generated"); AssetDatabase.Refresh(); }
            string output = AssetDatabase.GenerateUniqueAssetPath(Folder+"/Generated/"+SafeName(source.name));
            Directory.CreateDirectory(output);
            AssetDatabase.Refresh();
            var copy = Object.Instantiate(source.gameObject);
            copy.name = source.name+"_ShapeChange";
            Undo.RegisterCreatedObjectUndo(copy,"Create ShapeChange avatar");
            var originalRenderers = source.GetComponentsInChildren<Renderer>(true);
            var copyRenderers = copy.GetComponentsInChildren<Renderer>(true);
            var allMaterials = new Dictionary<(Material,int),Material>();
            int converted = 0;
            for (int ri=0; ri<originalRenderers.Length; ri++)
            {
                var original = originalRenderers[ri];
                if (!positions.ContainsKey(original)) continue;
                PrepareRenderer(source,original,copyRenderers[ri],copy,positions[original],normals[original],output,frame,measure,excluded.Contains(original),allMaterials);
                converted++;
            }
            // Assets with rest-coordinate UVs must survive mesh merging/UV removal in avatar optimizers.
            foreach (var component in copy.GetComponentsInChildren<Component>(true))
                if (component && component.GetType().FullName == "Anatawa12.AvatarOptimizer.TraceAndOptimize")
                    Object.DestroyImmediate(component);
            ConfigureModularAvatarBounds(copy,height);
            CalibrateHead(copy);
            if(closedSurface) ShapeChangeShell.Create(source,copy,included,output,frame,measure);
            ShapeChangePose.Calibrate(source,copy);
            PrepareAnimatedMaterials(source,copy,output);
            PrefabUtility.SaveAsPrefabAsset(copy,output+"/"+SafeName(copy.name)+".prefab");
            CreateClips(copy,output);
            ShapeChangePose.ExportClips(source,copy,output);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            SceneManager.MoveGameObjectToScene(copy,scene);
            var cameraObject = new GameObject("Preview Camera",typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject,scene);
            var camera = cameraObject.GetComponent<Camera>();
            camera.enabled = true;
            camera.transform.position = copy.transform.TransformPoint(new Vector3(0,height*0.55f,height*2.6f));
            camera.transform.LookAt(copy.transform.TransformPoint(new Vector3(0,height*0.55f,0)));
            camera.orthographic = true; camera.orthographicSize = height*copy.transform.lossyScale.y*0.8f;
            var lightObject = new GameObject("Preview Light",typeof(Light));
            SceneManager.MoveGameObjectToScene(lightObject,scene);
            var light = lightObject.GetComponent<Light>(); light.type=LightType.Directional; light.intensity=0.8f;
            light.transform.rotation=Quaternion.Euler(35,-30,0);
            // Separate the preview spatially; original loaded scene remains untouched.
            Vector3 offset = root.right*height*2.0f;
            copy.transform.position += offset; camera.transform.position += offset;
            EditorSceneManager.SaveScene(scene,output+"/Preview.unity");
            AssetDatabase.SaveAssets();
            Selection.activeGameObject=copy;
            SceneView.lastActiveSceneView?.Frame(new Bounds(copy.transform.TransformPoint(new Vector3(0,height*0.5f,0)),Vector3.one*height*1.6f),false);
            File.WriteAllText(output+"/Setup.txt",$"Source: {source.name}\nHeight: {height:F4}\nFrame: {frame}\nMeasure: {measure}\nConverted renderers: {converted}\nShape-preserving attachments: {string.Join(", ",excluded.Select(r=>r.name))}\nUV channels: 4,5,6,7 (Unity UI: UV5-UV8)\n");
            Debug.Log("[ShapeChange] Prepared "+converted+" renderers: "+output);
            return copy;
        }

        public static void ConfigureModularAvatarBounds(GameObject root,float height)
        {
            var settings=root.GetComponentsInChildren<Component>(true).Where(c=>c && c.GetType().FullName=="nadena.dev.modular_avatar.core.ModularAvatarMeshSettings").ToArray();
            if(settings.Length==0)return;
            var type=settings[0].GetType();var boundsMode=type.GetField("InheritBounds");
            var rootSettings=root.GetComponent(type)??root.AddComponent(type);
            // The shader's prepared coordinates and expanded bounds use the avatar root, including after MA processing.
            foreach(var setting in settings)boundsMode.SetValue(setting,Enum.Parse(boundsMode.FieldType,"Inherit"));
            boundsMode.SetValue(rootSettings,Enum.Parse(boundsMode.FieldType,"Set"));
            var rootBone=type.GetField("RootBone");var reference=Activator.CreateInstance(rootBone.FieldType);
            rootBone.FieldType.GetMethod("Set",new[]{typeof(GameObject)}).Invoke(reference,new object[]{root});
            rootBone.SetValue(rootSettings,reference);
            type.GetField("Bounds").SetValue(rootSettings,new Bounds(new Vector3(0,height*0.6f,0),Vector3.one*height*5));
        }

        public static Mesh PrepareMesh(Animator source,Renderer original,Vector3[] positions,Vector3[] normals,Vector4 frame,bool accessory)
        {
            float height=frame.w;
            var mesh=Object.Instantiate(SourceMesh(original));mesh.name=original.name+"_ShapeChange";
            var uv=Enumerable.Range(0,4).Select(_=>new List<Vector2>(mesh.vertexCount)).ToArray();
            var masks=BoneMasks(source,original);
            bool pairedEars=IsEar(original.name);
            // Keep paired ears/wrist/ankle accessories in separate frames.
            // ponytail: use seven body regions; split a Renderer when
            // unrelated decorations within one region require independent pivots.
            var groups=new int[positions.Length];var anchors=new Vector3[7];var anchorMasks=new Vector3[7];var anchorNormals=new Vector3[7];var counts=new int[7];
            for(int i=0;i<positions.Length;i++)
            {
                int group=masks[i].y>0.5f?(positions[i].x>=0?1:2):masks[i].z>0.5f?(positions[i].x>=0?3:4):pairedEars?(positions[i].x>=0?5:6):0;
                groups[i]=group;anchors[group]+=positions[i];anchorMasks[group]+=masks[i];anchorNormals[group]+=normals[i];counts[group]++;
            }
            for(int g=0;g<counts.Length;g++)if(counts[g]>0){anchors[g]/=counts[g];anchorMasks[g]/=counts[g];}
            if(accessory)
            {
                // Ear/tail art commonly keeps the root attached while the tip
                // bends. Anchor their frame at the first weighted appendage bone,
                // not at the long mesh's centre of mass.
                if(IsEarOrTail(original.name) && original is SkinnedMeshRenderer appendage)
                {
                    using(var weights=SourceMesh(original).GetAllBoneWeights())
                    {
                        var used=new HashSet<int>();foreach(var weight in weights)if(weight.weight>0.00001f)used.Add(weight.boneIndex);
                        for(int g=0;g<counts.Length;g++)if(counts[g]>0)
                        {
                            bool ear=pairedEars;
                            var candidates=used.Where(i=>i>=0 && i<appendage.bones.Length && appendage.bones[i])
                                .Select(i=>appendage.bones[i]).Where(b=>(ear?new[]{"ear","mimi","耳"}:new[]{"tail","sippo","尻尾"}).Any(part=>b.name.IndexOf(part,StringComparison.OrdinalIgnoreCase)>=0))
                                .Where(b=>!ear || (source.transform.InverseTransformPoint(b.position).x>=0)==(g==5))
                                .OrderBy(b=>AnimationUtility.CalculateTransformPath(b,source.transform).Count(c=>c=='/')).ToArray();
                            if(candidates.Length>0)anchors[g]=source.transform.InverseTransformPoint(candidates[0].position);
                        }
                    }
                }
                var distances=Enumerable.Repeat(float.PositiveInfinity,counts.Length).ToArray();
                var contacts=anchors.ToArray();var contactMasks=anchorMasks.ToArray();
                // Project onto the actual triangle, including its interpolated
                // masks/normal. Nearest vertices make pins jump between tessellation
                // corners and attach long tails away from their root.
                foreach(var owner in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if(owner==original || !owner.enabled || !owner.gameObject.activeInHierarchy || IsAccessory(owner.name) || IsEarOrTail(owner.name))continue;
                    var baked=new Mesh();
                    try
                    {
                        owner.BakeMesh(baked);var matrix=source.transform.worldToLocalMatrix*owner.transform.localToWorldMatrix;
                        var vertices=baked.vertices.Select(matrix.MultiplyPoint3x4).ToArray();var surfaceNormals=baked.normals;var ownerMasks=BoneMasks(source,owner);
                        var indices=baked.triangles;
                        for(int i=0;i<indices.Length;i+=3)
                        {
                            int a=indices[i],b=indices[i+1],c=indices[i+2];
                            for(int g=0;g<counts.Length;g++)
                            {
                                if(counts[g]==0)continue;
                                var bary=ClosestBarycentric(anchors[g],vertices[a],vertices[b],vertices[c]);
                                var mask=ownerMasks[a]*bary.x+ownerMasks[b]*bary.y+ownerMasks[c]*bary.z;
                                if((mask-anchorMasks[g]).sqrMagnitude>0.2f)continue;
                                var point=vertices[a]*bary.x+vertices[b]*bary.y+vertices[c]*bary.z;
                                float distance=(point-anchors[g]).sqrMagnitude;
                                if(distance>=distances[g])continue;
                                distances[g]=distance;contacts[g]=point;contactMasks[g]=mask;
                                anchorNormals[g]=matrix.inverse.transpose.MultiplyVector(surfaceNormals[a]*bary.x+surfaceNormals[b]*bary.y+surfaceNormals[c]*bary.z).normalized;
                            }
                        }
                    }
                    finally{Object.DestroyImmediate(baked);}
                }
                anchors=contacts;anchorMasks=contactMasks;
            }
            for(int i=0;i<mesh.vertexCount;i++)
            {
                Vector3 p=((accessory?anchors[groups[i]]:positions[i])-(Vector3)frame)/height;
                Vector3 mask=accessory?anchorMasks[groups[i]]:masks[i],n=accessory?anchorNormals[groups[i]].normalized:normals[i];
                if(n.sqrMagnitude<0.001f)n=Vector3.up;
                n/=Mathf.Abs(n.x)+Mathf.Abs(n.y)+Mathf.Abs(n.z);
                Vector2 oct=new Vector2(n.x,n.y);
                if(n.z<0)oct=new Vector2((1-Mathf.Abs(n.y))*(n.x>=0?1:-1),(1-Mathf.Abs(n.x))*(n.y>=0?1:-1));
                uv[0].Add(new Vector2(p.x,p.y));uv[1].Add(new Vector2(p.z,mask.x));uv[2].Add(new Vector2(mask.y,mask.z));uv[3].Add(oct);
            }
            for(int ch=0;ch<4;ch++)mesh.SetUVs(ch+4,uv[ch]);
            if(accessory && uv[3].Distinct().Count()>7)throw new InvalidOperationException("Attachment normal groups inconsistent: "+original.name);
            return mesh;
        }

        public static Vector3 ClosestBarycentric(Vector3 p,Vector3 a,Vector3 b,Vector3 c)
        {
            var ab=b-a;var ac=c-a;var ap=p-a;
            float d1=Vector3.Dot(ab,ap),d2=Vector3.Dot(ac,ap);
            if(d1<=0 && d2<=0)return new Vector3(1,0,0);
            var bp=p-b;float d3=Vector3.Dot(ab,bp),d4=Vector3.Dot(ac,bp);
            if(d3>=0 && d4<=d3)return new Vector3(0,1,0);
            float vc=d1*d4-d3*d2;
            if(vc<=0 && d1>=0 && d3<=0){float v=d1/Mathf.Max(d1-d3,1e-20f);return new Vector3(1-v,v,0);}
            var cp=p-c;float d5=Vector3.Dot(ab,cp),d6=Vector3.Dot(ac,cp);
            if(d6>=0 && d5<=d6)return new Vector3(0,0,1);
            float vb=d5*d2-d1*d6;
            if(vb<=0 && d2>=0 && d6<=0){float w=d2/Mathf.Max(d2-d6,1e-20f);return new Vector3(1-w,0,w);}
            float va=d3*d6-d5*d4;
            if(va<=0 && d4-d3>=0 && d5-d6>=0){float w=(d4-d3)/Mathf.Max(d4-d3+d5-d6,1e-20f);return new Vector3(0,1-w,w);}
            float sum=va+vb+vc;if(Mathf.Abs(sum)<1e-20f)return new Vector3(1,0,0);
            return new Vector3(va,vb,vc)/sum;
        }

        static SkinnedMeshRenderer PrepareRenderer(Animator source,Renderer original,Renderer target,GameObject root,Vector3[] positions,Vector3[] normals,string output,Vector4 frame,Vector4 measure,bool accessory,Dictionary<(Material,int),Material> cache,Material template=null)
        {
            float height=frame.w;
            var mesh=PrepareMesh(source,original,positions,normals,frame,accessory);
            var dst=target as SkinnedMeshRenderer;
            var previousMesh=dst?dst.sharedMesh:null;
            if(previousMesh && AssetDatabase.GetAssetPath(previousMesh).StartsWith(output+"/",StringComparison.Ordinal))
            {
                mesh.name=Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(previousMesh));EditorUtility.CopySerialized(mesh,previousMesh);
                // CopySerialized can retain a loaded Mesh's old vertex-channel
                // cache. Explicitly refresh the preparation channels as well.
                for(int ch=4;ch<8;ch++){var data=new List<Vector2>();mesh.GetUVs(ch,data);previousMesh.SetUVs(ch,data);}
                Object.DestroyImmediate(mesh);mesh=previousMesh;EditorUtility.SetDirty(mesh);
            }
            else AssetDatabase.CreateAsset(mesh,AssetDatabase.GenerateUniqueAssetPath(output+"/"+SafeName(mesh.name)+".asset"));
            if(!dst)
            {
                var materials=target.sharedMaterials;
                bool wasEnabled=target.enabled;
                dst=target.gameObject.AddComponent<SkinnedMeshRenderer>();dst.sharedMaterials=materials;
                dst.enabled=wasEnabled;
            }
            if(!(original is SkinnedMeshRenderer))
            {
                mesh.bindposes=new[]{Matrix4x4.identity};
                mesh.boneWeights=Enumerable.Repeat(new BoneWeight{boneIndex0=0,weight0=1},mesh.vertexCount).ToArray();
                dst.bones=new[]{dst.transform};
                if(target && target!=dst)Object.DestroyImmediate(target);
                Object.DestroyImmediate(dst.GetComponent<MeshFilter>());
            }
            dst.sharedMesh=mesh;dst.rootBone=root.transform;
            dst.localBounds=new Bounds(new Vector3(0,height*0.6f,0),Vector3.one*height*5);dst.updateWhenOffscreen=true;
            var previousMaterials=dst.sharedMaterials;
            var otherMaterials=root.GetComponentsInChildren<Renderer>(true).Where(r=>r!=dst).SelectMany(r=>r.sharedMaterials).Where(m=>m).ToArray();
            Texture2D vertexData=null;
            if(accessory)
            {
                if(mesh.tangents.Length!=mesh.vertexCount && mesh.HasVertexAttribute(VertexAttribute.TexCoord0))mesh.RecalculateTangents();
                var tangents=mesh.tangents;var localNormals=mesh.normals;
                if(tangents.Length!=mesh.vertexCount)tangents=new Vector4[mesh.vertexCount];
                for(int v=0;v<tangents.Length;v++)if(((Vector3)tangents[v]).sqrMagnitude<0.000001f)
                {
                    Vector3 n=localNormals[v],t=Vector3.Cross(Mathf.Abs(n.y)<0.9f?Vector3.up:Vector3.right,n).normalized;
                    tangents[v]=new Vector4(t.x,t.y,t.z,1);
                }
                mesh.tangents=tangents;
                EditorUtility.SetDirty(mesh);
                int width=Mathf.Min(2048,Mathf.NextPowerOfTwo(mesh.vertexCount)),heightRows=(mesh.vertexCount+width-1)/width*3;
                var data=new Texture2D(width,heightRows,TextureFormat.RGBAFloat,false,true){name=original.name+"_AttachmentFrames",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
                var pixels=new Color[width*heightRows];
                var bakedFrames=new Mesh();Vector4[] frameTangents;
                try{dst.BakeMesh(bakedFrames);frameTangents=bakedFrames.tangents;}
                finally{Object.DestroyImmediate(bakedFrames);}
                if(frameTangents.Length!=mesh.vertexCount)throw new InvalidOperationException("装飾の接線を取得できません: "+original.name);
                var matrix=root.transform.worldToLocalMatrix*dst.transform.localToWorldMatrix;
                for(int v=0;v<mesh.vertexCount;v++)
                {
                    int pixel=v%width+(v/width)*3*width;
                    Vector3 p=(positions[v]-(Vector3)frame)/frame.w,n=normals[v];
                    Vector3 t=matrix.MultiplyVector(frameTangents[v]);
                    pixels[pixel]=new Color(p.x,p.y,p.z,1);
                    pixels[pixel+width]=new Color(n.x,n.y,n.z,0);
                    pixels[pixel+width*2]=new Color(t.x,t.y,t.z,0);
                }
                data.SetPixels(pixels);data.Apply(false,false);
                vertexData=previousMaterials.Where(m=>m && m.HasProperty("_SCVertexData")).Select(m=>m.GetTexture("_SCVertexData") as Texture2D)
                    .FirstOrDefault(t=>t && AssetDatabase.GetAssetPath(t).StartsWith(output+"/",StringComparison.Ordinal)
                        && !otherMaterials.Any(m=>m.HasProperty("_SCVertexData") && m.GetTexture("_SCVertexData")==t));
                if(vertexData){EditorUtility.CopySerialized(data,vertexData);Object.DestroyImmediate(data);EditorUtility.SetDirty(vertexData);}
                else{vertexData=data;AssetDatabase.CreateAsset(vertexData,AssetDatabase.GenerateUniqueAssetPath(output+"/"+SafeName(data.name)+".asset"));}
            }
            var mats=original.sharedMaterials.ToArray();
            for(int i=0;i<mats.Length;i++)
            {
                if(!mats[i])continue;
                int role=accessory?1:IsEarOrTail(original.name)?2:0;
                // Vertex lookup data belongs to one mesh, so attachment materials cannot be shared across renderers.
                var key=(mats[i],accessory?original.GetInstanceID():role);
                if(!cache.TryGetValue(key,out var mat))
                {
                    mat=new Material(mats[i]){name=mats[i].name+(role==1?"_Attachment":role==2?"_Integrated":"_ShapeChange")};
                    mat.parent=null;
                    mat.shader=ConvertedShader(mats[i]);
                    mat.SetVector("_SCFrame",frame);mat.SetVector("_SCMeasure",measure);
                    mat.SetFloat("_SCEnabled",1);mat.SetFloat("_SCAmount",0);mat.SetFloat("_SCArms",1);mat.SetFloat("_SCLegs",1);
                    mat.SetFloat("_SCPreserveSurface",1);mat.SetFloat("_SCFaceCoverage",0.4f);mat.SetFloat("_SCSurfaceFollow",0.12f);mat.SetFloat("_SCFaceStart",0.44f);
                    mat.SetFloat("_SCBodyBase",Mathf.Clamp((BonePosition(source,HumanBodyBones.Hips).y-frame.y)/height-0.12f,0.1f,0.65f));
                    if(template)for(int property=0;property<template.shader.GetPropertyCount();property++)
                    {
                        string name=template.shader.GetPropertyName(property);if(!name.StartsWith("_SC") || !mat.HasProperty(name))continue;
                        if(name=="_SCPanelProfile")mat.SetTexture(name,template.GetTexture(name));
                        else if(template.shader.GetPropertyType(property)==ShaderPropertyType.Vector)mat.SetVector(name,template.GetVector(name));
                        else if(template.shader.GetPropertyType(property)!=ShaderPropertyType.Texture)mat.SetFloat(name,template.GetFloat(name));
                    }
                    mat.SetFloat("_SCReady",1);mat.SetFloat("_SCProxy",0);mat.SetFloat("_SCAccessory",accessory?1:0);
                    mat.SetFloat("_SCAppendage",role==2?1:0);
                    mat.SetFloat("_SCAttachmentDataReady",accessory?1:0);
                    if(accessory)mat.SetTexture("_SCVertexData",vertexData);
                    var previous=i<previousMaterials.Length?previousMaterials[i]:null;
                    if(previous && AssetDatabase.GetAssetPath(previous).StartsWith(output+"/",StringComparison.Ordinal) && (!accessory || !otherMaterials.Contains(previous)))
                    {mat.name=Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(previous));EditorUtility.CopySerialized(mat,previous);Object.DestroyImmediate(mat);mat=previous;EditorUtility.SetDirty(mat);}
                    else AssetDatabase.CreateAsset(mat,AssetDatabase.GenerateUniqueAssetPath(output+"/"+SafeName(mat.name)+".mat"));
                    cache.Add(key,mat);
                }
                mats[i]=mat;
            }
            dst.sharedMaterials=mats;return dst;
        }

        public static void PrepareAnimatedMaterials(Animator source,GameObject root,string output)
        {
            var cache=new Dictionary<(Material,Renderer,int),Material>();
            ShapeChangeAnimations.Prepare(source.gameObject,root,output,(original,renderer,slot)=>
            {
                var key=(original,renderer,slot);
                if(cache.TryGetValue(key,out var cached))return cached;
                var template=renderer.sharedMaterials[slot];
                var shader=ConvertedShader(original);
                if(!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(original,out string guid,out long localId))
                    throw new InvalidOperationException("保存済みの差替え元Materialが必要です。");
                string rendererPath=AnimationUtility.CalculateTransformPath(renderer.transform,root.transform);
                string id=guid+"_"+localId+"_"+Hash128.Compute(rendererPath+"|"+slot);
                string path=output+"/AnimationMaterials/SCMat_"+id+".mat";
                string tag="ShapeChangeAnimationMaterial:"+id;
                var existing=AssetDatabase.LoadAssetAtPath<Material>(path);
                if((existing || File.Exists(Path.GetFullPath(path))) && (!existing || AssetImporter.GetAtPath(path)?.userData!=tag))
                    throw new InvalidOperationException("所有を確認できない差替え素材を上書きしません: "+path);
                Material copy=null;
                try
                {
                    copy=new Material(original){name=original.name+"_ShapeChangeAnimation"};
                    copy.parent=null;copy.shader=shader;
                    for(int i=0;i<template.shader.GetPropertyCount();i++)
                    {
                        string name=template.shader.GetPropertyName(i);
                        if(!name.StartsWith("_SC",StringComparison.Ordinal) || !copy.HasProperty(name))continue;
                        switch(template.shader.GetPropertyType(i))
                        {
                            case ShaderPropertyType.Texture:
                                copy.SetTexture(name,template.GetTexture(name));
                                copy.SetTextureScale(name,template.GetTextureScale(name));
                                copy.SetTextureOffset(name,template.GetTextureOffset(name));break;
                            case ShaderPropertyType.Vector:copy.SetVector(name,template.GetVector(name));break;
                            case ShaderPropertyType.Color:copy.SetColor(name,template.GetColor(name));break;
                            default:copy.SetFloat(name,template.GetFloat(name));break;
                        }
                    }
                    if(existing)
                    {
                        EditorUtility.CopySerialized(copy,existing);EditorUtility.SetDirty(existing);
                        cache.Add(key,existing);return existing;
                    }
                    AssetDatabase.CreateAsset(copy,path);
                    var importer=AssetImporter.GetAtPath(path);
                    if(!importer)throw new InvalidOperationException("差替え素材のImporterがありません: "+path);
                    importer.userData=tag;AssetDatabase.WriteImportSettingsIfDirty(path);
                    cache.Add(key,copy);return copy;
                }
                finally{if(copy && !EditorUtility.IsPersistent(copy))Object.DestroyImmediate(copy);}
            });
        }

        [MenuItem("Tools/ShapeChange/Update Accessory Following")]
        public static void UpdateAccessoryPreview()
        {
            var root=Object.FindObjectsOfType<Animator>().FirstOrDefault(a=>a.name.EndsWith("_ShapeChange"));
            if(!root)throw new InvalidOperationException("セットアップ済みの複製がありません。");
            var source=Object.FindObjectsOfType<Animator>().SingleOrDefault(a=>a.isHuman && root.name==a.name+"_ShapeChange");
            UpdateAccessories(source,root.gameObject,false);
        }

        public static void UpdateAccessories(Animator source,GameObject root,bool overwriteUV,bool refreshPrepared=true)
        {
            if(!source || !root || source.gameObject==root)throw new InvalidOperationException("元アバターと専用の複製を指定してください。");
            if(root.name!=source.name+"_ShapeChange")throw new InvalidOperationException("複製と同じ名前の元アバターを指定してください。");
            var template=Materials(root).First(m=>m.GetFloat("_SCProxy")<0.5f && m.GetFloat("_SCAccessory")<0.5f);
            string output=Path.GetDirectoryName(AssetDatabase.GetAssetPath(template)).Replace('\\','/');
            if(!output.StartsWith(Folder+"/Generated/",StringComparison.Ordinal))throw new InvalidOperationException("Generated以下の専用素材が必要です。");
            var candidates=new List<(Renderer original,Renderer target)>();
            foreach(var original in source.GetComponentsInChildren<Renderer>(true))
            {
                if(!(original is SkinnedMeshRenderer) && !(original is MeshRenderer))continue;
                string path=AnimationUtility.CalculateTransformPath(original.transform,source.transform);
                var target=root.transform.Find(path)?.GetComponent<Renderer>();
                if(!target)continue;
                var existing=target.sharedMaterials.FirstOrDefault(m=>m && m.HasProperty("_SCReady") && m.GetFloat("_SCReady")>0);
                if(existing && !(refreshPrepared && existing.GetFloat("_SCAccessory")>0.5f) && (!IsEarOrTail(original.name) || existing.GetFloat("_SCAccessory")>0.5f || existing.GetFloat("_SCAppendage")>0.5f))continue;
                var mesh=SourceMesh(original);
                for(int ch=4;ch<8;ch++)if(!overwriteUV && mesh.HasVertexAttribute((VertexAttribute)((int)VertexAttribute.TexCoord0+ch)))throw new InvalidOperationException(original.name+" はUV"+(ch+1)+"を使用中です。");
                foreach(var mat in original.sharedMaterials.Where(m=>m))ConvertedShader(mat);
                candidates.Add((original,target));
            }
            var changed=new List<SkinnedMeshRenderer>();var cache=new Dictionary<(Material,int),Material>();
            foreach(var pair in candidates)
            {
                var baked=new Mesh();var mesh=SourceMesh(pair.original);
                if(pair.original is SkinnedMeshRenderer skin){skin.BakeMesh(baked);mesh=baked;}
                var matrix=source.transform.worldToLocalMatrix*pair.original.transform.localToWorldMatrix;
                var positions=mesh.vertices.Select(matrix.MultiplyPoint3x4).ToArray();
                var normals=mesh.normals.Select(matrix.inverse.transpose.MultiplyVector).Select(n=>n.normalized).ToArray();
                Object.DestroyImmediate(baked);
                bool accessory=pair.target.sharedMaterials.Any(m=>m && m.HasProperty("_SCAccessory") && m.GetFloat("_SCAccessory")>0.5f) || !IsEarOrTail(pair.original.name);
                changed.Add(PrepareRenderer(source,pair.original,pair.target,root,positions,normals,output,template.GetVector("_SCFrame"),template.GetVector("_SCMeasure"),accessory,cache,template));
            }
            PrepareAnimatedMaterials(source,root,output);
            ConfigureModularAvatarBounds(root,template.GetVector("_SCFrame").w);
            string prefabPath=output+"/"+SafeName(root.name)+".prefab";
            if(File.Exists(prefabPath))
            {
                var prefab=PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    foreach(var r in changed)
                    {
                        string path=AnimationUtility.CalculateTransformPath(r.transform,root.transform);
                        var go=prefab.transform.Find(path).gameObject;
                        var dst=go.GetComponent<SkinnedMeshRenderer>();
                        if(!dst){Object.DestroyImmediate(go.GetComponent<MeshRenderer>());Object.DestroyImmediate(go.GetComponent<MeshFilter>());dst=go.AddComponent<SkinnedMeshRenderer>();}
                        dst.sharedMesh=r.sharedMesh;dst.sharedMaterials=r.sharedMaterials;dst.enabled=r.enabled;
                        dst.bones=r.bones.Select(b=>b==root.transform?prefab.transform:prefab.transform.Find(AnimationUtility.CalculateTransformPath(b,root.transform))).ToArray();
                        dst.rootBone=prefab.transform;dst.localBounds=r.localBounds;dst.updateWhenOffscreen=true;
                    }
                    PrepareAnimatedMaterials(source,prefab,output);
                    ConfigureModularAvatarBounds(prefab,template.GetVector("_SCFrame").w);
                    PrefabUtility.SaveAsPrefabAsset(prefab,prefabPath);
                }
                finally{PrefabUtility.UnloadPrefabContents(prefab);}
            }
            CreateClips(root,output);AssetDatabase.SaveAssets();
            if(root.scene.path.StartsWith(Folder+"/Generated/",StringComparison.Ordinal))EditorSceneManager.SaveScene(root.scene);
            File.AppendAllText(output+"/Setup.txt","Attachment/integration settings updated: "+string.Join(", ",changed.Select(r=>r.name))+"\n");
            AssetDatabase.ImportAsset(output+"/Setup.txt",ImportAssetOptions.ForceUpdate);
            foreach(var window in Resources.FindObjectsOfTypeAll<ShapeChangeSetup>())if(window.avatar==source && window.preview==root)
            {
                window.excluded.Clear();
                foreach(var original in source.GetComponentsInChildren<Renderer>(true))
                {
                    var r=root.transform.Find(AnimationUtility.CalculateTransformPath(original.transform,source.transform))?.GetComponent<Renderer>();
                    if(r && r.sharedMaterials.Any(m=>m && m.HasProperty("_SCAccessory") && m.GetFloat("_SCAccessory")>0.5f))window.excluded.Add(original);
                }
            }
            Debug.Log("[ShapeChange] 追従・形状統合設定を更新: "+string.Join(", ",changed.Select(r=>r.name)));
        }

        internal static Dictionary<Transform,Transform> MergeArmatureAliases(GameObject root)
        {
            var aliases=new Dictionary<Transform,Transform>();
            // Use MA's declared correspondence without requiring MA in the shader-only package.
            foreach(var component in root.GetComponentsInChildren<Component>(true))
                if(component && component.GetType().FullName=="nadena.dev.modular_avatar.core.ModularAvatarMergeArmature")
                {
                    var method=component.GetType().GetMethod("GetBonesMapping") ?? throw new InvalidOperationException("MAのボーン対応APIがありません: "+component.name);
                    var pairs=(IEnumerable<(Transform,Transform)>)method.Invoke(component,null);
                    if(pairs==null)throw new InvalidOperationException("MA Merge Armatureの統合先を確認してください: "+component.name);
                    foreach(var pair in pairs)if(pair.Item1 && pair.Item2)aliases[pair.Item2]=pair.Item1;
                }
            return aliases;
        }

        static Vector3[] BoneMasks(Animator avatar, Renderer renderer)
        {
            var mesh = SourceMesh(renderer);
            var result = new Vector3[mesh.vertexCount];
            var head = avatar.GetBoneTransform(HumanBodyBones.Head);
            var neck = avatar.GetBoneTransform(HumanBodyBones.Neck);
            var la = avatar.GetBoneTransform(HumanBodyBones.LeftUpperArm); var ra=avatar.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var ll=avatar.GetBoneTransform(HumanBodyBones.LeftUpperLeg); var rl=avatar.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            var aliases=MergeArmatureAliases(avatar.gameObject);
            bool UnderMerged(Transform bone,Transform region)
            {
                if(aliases.Count==0)return Under(bone,region);
                var visited=new HashSet<Transform>();
                while(bone && visited.Add(bone))
                {
                    if(Under(bone,region))return true;
                    bone=aliases.TryGetValue(bone,out var target)?target:bone.parent;
                }
                return false;
            }
            Func<Transform,Vector3> classify = b => new Vector3(UnderMerged(b,head)?1:UnderMerged(b,neck)?0.5f:0,UnderMerged(b,la)||UnderMerged(b,ra)?1:0,UnderMerged(b,ll)||UnderMerged(b,rl)?1:0);
            if (!(renderer is SkinnedMeshRenderer skin))
            {
                for(int i=0;i<result.Length;i++) result[i]=classify(renderer.transform);
                return result;
            }
            var bones = skin.bones.Select(classify).ToArray();
            using (var counts=mesh.GetBonesPerVertex())
            using (var weights=mesh.GetAllBoneWeights())
            {
                int offset=0;
                for(int v=0;v<result.Length;v++)
                    for(int j=0;j<counts[v];j++)
                    {
                        var w=weights[offset++];
                        if(w.boneIndex>=0 && w.boneIndex<bones.Length) result[v]+=bones[w.boneIndex]*w.weight;
                    }
            }
            return result;
        }

        public static void CreateClips(GameObject root, string output)
        {
            for(int mode=-1;mode<Modes.Length;mode++)
            {
                string name=mode<0?"Original":"Shape_"+mode.ToString("D2"),pathToClip=output+"/"+name+".anim";
                var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(pathToClip);
                bool create=!clip;if(create)clip=new AnimationClip{name=name};
                WriteShapeCurves(root,clip,mode,mode<0?0:1);
                if(create)AssetDatabase.CreateAsset(clip,pathToClip);else EditorUtility.SetDirty(clip);
            }
        }

        internal static void WriteShapeCurves(GameObject root, AnimationClip clip, int mode, float amount, bool enable = false)
        {
            foreach(var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if(!renderer.sharedMaterials.Any(m=>m && m.HasProperty("_SCReady") && m.GetFloat("_SCReady")>0))continue;
                string path=AnimationUtility.CalculateTransformPath(renderer.transform,root.transform);
                void SetCurve(string property,float value) => AnimationUtility.SetEditorCurve(clip,
                    EditorCurveBinding.FloatCurve(path,typeof(SkinnedMeshRenderer),"material."+property),AnimationCurve.Constant(0,1,value));
                SetCurve("_SCAmount",amount);
                if(mode>=0)SetCurve("_SCMode",mode);
                if(enable)SetCurve("_SCEnabled",1);
            }
        }

        static string SafeName(string name) => string.Concat(name.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c));
    }
}
