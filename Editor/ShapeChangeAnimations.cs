// shortcut: Material swaps require saved controllers and external .anim clips on skinned renderers; extend when another supported avatar needs a different graph.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ShapeChange
{
    public static class ShapeChangeAnimations
    {
        const string Descriptor = "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor";
        const string MergeAnimator = "nadena.dev.modular_avatar.core.ModularAvatarMergeAnimator";
        static readonly Regex Slot = new Regex(@"^m_Materials\.Array\.data\[(\d+)\]$");

        public static string CreateToggleController(GameObject root, int mode)
        {
            if(!root || mode<0 || mode>=ShapeChangeSetup.Modes.Length)
                throw new ArgumentException("セットアップ済みの複製と形状を指定してください。");
            var material=ShapeChangeSetup.Materials(root).FirstOrDefault();
            var assetPath=material?AssetDatabase.GetAssetPath(material):"";
            if(!assetPath.StartsWith(ShapeChangeSetup.Folder+"/Generated/",StringComparison.Ordinal))
                throw new InvalidOperationException("Setup Avatarで作った複製を選んでください。");
            string folder=Path.GetDirectoryName(assetPath).Replace('\\','/');
            string path=AssetDatabase.GenerateUniqueAssetPath(folder+"/ShapeToggle_"+mode.ToString("D2")+".controller");
            var offClip=new AnimationClip{name="Off"};
            var onClip=new AnimationClip{name="On"};
            try
            {
                ShapeChangeSetup.WriteShapeCurves(root,offClip,mode,0,true);
                ShapeChangeSetup.WriteShapeCurves(root,onClip,mode,1,true);
                if(AnimationUtility.GetCurveBindings(onClip).Length==0)
                    throw new InvalidOperationException("変形用のRendererが見つかりません。");
                var controller=AnimatorController.CreateAnimatorControllerAtPath(path);
                controller.AddParameter("ShapeChange",AnimatorControllerParameterType.Bool);
                AssetDatabase.AddObjectToAsset(offClip,controller);
                AssetDatabase.AddObjectToAsset(onClip,controller);
                var machine=controller.layers[0].stateMachine;
                var off=machine.AddState("Off");
                var on=machine.AddState("On");
                off.motion=offClip;on.motion=onClip;
                off.writeDefaultValues=on.writeDefaultValues=false;
                machine.defaultState=off;
                Connect(off,on,AnimatorConditionMode.If);
                Connect(on,off,AnimatorConditionMode.IfNot);
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssetIfDirty(controller);
                return path;
            }
            catch
            {
                if(AssetDatabase.LoadMainAssetAtPath(path))AssetDatabase.DeleteAsset(path);
                if(offClip && !EditorUtility.IsPersistent(offClip))Object.DestroyImmediate(offClip);
                if(onClip && !EditorUtility.IsPersistent(onClip))Object.DestroyImmediate(onClip);
                throw;
            }

            void Connect(AnimatorState from,AnimatorState to,AnimatorConditionMode condition)
            {
                var transition=from.AddTransition(to);
                transition.hasExitTime=false;
                transition.hasFixedDuration=true;
                transition.duration=.25f;
                transition.AddCondition(condition,0,"ShapeChange");
            }
        }

        sealed class Reference
        {
            public Component Owner;
            public string Property;
            public RuntimeAnimatorController Controller;
        }

        // Material callback: clone the ORIGINAL material, preserve its expression
        // properties, then copy every _SC value (including textures) from this
        // target renderer/slot. Reuse by source-material + target-renderer context.
        // Call after calibration, outside StartAssetEditing, before saving target.
        public static void Prepare(GameObject source, GameObject target, string output,
            Func<Material, Renderer, int, Material> convertMaterial)
        {
            if (!source || !target || source == target || convertMaterial == null)
                throw new InvalidOperationException("元と専用複製、素材変換処理が必要です。");
            output = output.Replace('\\', '/').TrimEnd('/');
            if (!output.StartsWith(ShapeChangeSetup.Folder + "/Generated/", StringComparison.Ordinal)
                || output.Split('/').Any(p => p == ".." || p == "."))
                throw new InvalidOperationException("専用Generated出力だけを指定してください。");
            string folder = output + "/AnimationMaterials";
            var contexts = new Dictionary<Transform, Context>();
            // Always discover ORIGINAL controllers; never clone an earlier copy.
            foreach (var entry in References(source))
            {
                if (!entry.Controller.animationClips.Any(HasMaterialSlots)) continue;
                Transform sourceBase = AnimationRoot(entry.Owner, source.transform);
                var targetBase = Find(target.transform, AnimationUtility.CalculateTransformPath(sourceBase, source.transform));
                if (!targetBase) throw new InvalidOperationException("複製側のAnimation rootがありません。");
                if (!contexts.TryGetValue(sourceBase, out var context))
                    contexts.Add(sourceBase, context = new Context(targetBase, folder,
                        AnimationUtility.CalculateTransformPath(sourceBase, source.transform), convertMaterial));
                var owners = entry.Owner.GetComponents(entry.Owner.GetType());
                int ordinal = Array.IndexOf(owners, entry.Owner);
                var node = Find(target.transform, AnimationUtility.CalculateTransformPath(entry.Owner.transform, source.transform));
                var matches = node ? node.GetComponents(entry.Owner.GetType()) : Array.Empty<Component>();
                if (ordinal < 0 || ordinal >= matches.Length)
                    throw new InvalidOperationException("複製側のController所有Componentがありません。");
                var converted = context.Controller(entry.Controller);
                using (var so = new SerializedObject(matches[ordinal]))
                {
                    var property = so.FindProperty(entry.Property);
                    if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
                        throw new InvalidOperationException("Controller参照の保存先がありません: " + entry.Property);
                    property.objectReferenceValue = converted;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        // Add to ShapeChangeSetup.Materials(root), after its renderer materials.
        // Read-only dependency collection; no per-GUI PPtr curve rewriting.
        public static IEnumerable<Material> Materials(GameObject root)
        {
            var controllers = References(root).Select(r => (Object)r.Controller).Distinct().ToArray();
            if (controllers.Length == 0) return Enumerable.Empty<Material>();
            return EditorUtility.CollectDependencies(controllers).OfType<Material>()
                .Where(m => m && m.HasProperty("_SCReady") && m.GetFloat("_SCReady") > 0).Distinct();
        }

        static IEnumerable<Reference> References(GameObject root)
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (!component || (!(component is Animator) && !(component is MonoBehaviour))) continue;
                using (var so = new SerializedObject(component))
                {
                    var p = so.GetIterator();
                    while (p.Next(true))
                        if (p.propertyType == SerializedPropertyType.ObjectReference
                            && p.objectReferenceValue is RuntimeAnimatorController controller)
                            yield return new Reference { Owner = component, Property = p.propertyPath, Controller = controller };
                }
            }
        }

        static Transform AnimationRoot(Component owner, Transform avatar)
        {
            if (owner is Animator) return owner.transform;
            if (owner.GetType().FullName == Descriptor) return avatar;
            if (owner.GetType().FullName != MergeAnimator)
                throw new InvalidOperationException("Animation rootが未対応のController所有型: " + owner.GetType().FullName);
            var type = owner.GetType();
            string mode = type.GetField("pathMode")?.GetValue(owner)?.ToString();
            if (mode == "Absolute") return avatar;
            if (mode != "Relative") throw new InvalidOperationException("MA pathModeを解決できません。");
            var reference = type.GetField("relativePathRoot")?.GetValue(owner);
            var get = reference?.GetType().GetMethod("Get", new[] { typeof(Component) });
            if (get == null) throw new InvalidOperationException("MA relativePathRoot APIを解決できません。");
            var resolved = get.Invoke(reference, new object[] { avatar }) as GameObject;
            var result = resolved ? resolved.transform : owner.transform;
            if (result != avatar && !result.IsChildOf(avatar))
                throw new InvalidOperationException("MA Animation rootが元アバター外です。");
            return result;
        }

        static Transform Find(Transform root, string path) => string.IsNullOrEmpty(path) ? root : root.Find(path);
        static bool HasMaterialSlots(AnimationClip clip) => clip && AnimationUtility.GetObjectReferenceCurveBindings(clip)
            .Where(b => typeof(Renderer).IsAssignableFrom(b.type) && Slot.IsMatch(b.propertyName))
            .Any(b => AnimationUtility.GetObjectReferenceCurve(clip, b).Any(k => k.value is Material));
        static bool GraphObject(Object o) => o is RuntimeAnimatorController || o is Motion
            || o is AnimatorState || o is AnimatorStateMachine || o is AnimatorTransitionBase || o is StateMachineBehaviour;
        static string Digest(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        static string Identity(Object asset)
        {
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long id))
                throw new InvalidOperationException("保存済みAnimation assetが必要です: " + asset.name);
            return guid + "_" + id;
        }
        static string Absolute(string assetPath) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));

        sealed class Context
        {
            readonly Transform root;
            readonly string folder, context;
            readonly Func<Material, Renderer, int, Material> material;
            readonly Dictionary<RuntimeAnimatorController, RuntimeAnimatorController> controllers = new Dictionary<RuntimeAnimatorController, RuntimeAnimatorController>();
            public Context(Transform root, string folder, string context, Func<Material, Renderer, int, Material> material)
            { this.root = root; this.folder = folder; this.context = context; this.material = material; }

            public RuntimeAnimatorController Controller(RuntimeAnimatorController source)
            {
                if (controllers.TryGetValue(source, out var result)) return result;
                string originalPath = AssetDatabase.GetAssetPath(source);
                if (!(source is AnimatorController) || !AssetDatabase.IsMainAsset(source)
                    || !originalPath.EndsWith(".controller", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Material差替えを含むAOC/未保存/非controller assetは未対応です: " + source.name);
                if ((AssetImporter.GetAtPath(originalPath)?.userData ?? "").StartsWith("ShapeChangeAnimations:", StringComparison.Ordinal))
                    throw new InvalidOperationException("以前の専用controllerを元として再複製しません。");
                var dependencies = EditorUtility.CollectDependencies(new Object[] { source });
                if (dependencies.Where(GraphObject).Any(EditorUtility.IsDirty))
                    throw new InvalidOperationException("元Animation graphに未保存編集があります。元を自動保存せず停止します。");
                var clips = source.animationClips.Where(HasMaterialSlots).Distinct().ToArray();
                if (clips.Any(c => AssetDatabase.GetAssetPath(c) == originalPath
                    || !AssetDatabase.IsMainAsset(c) || !AssetDatabase.GetAssetPath(c).EndsWith(".anim", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("埋込/FBX等のMaterial差替えClipは未対応です。");
                var internalAssets = AssetDatabase.LoadAllAssetsAtPath(originalPath).Where(o => o).ToArray();
                if (internalAssets.OfType<AnimatorState>().Any(s => s.behaviours.Any(b => !b))
                    || internalAssets.OfType<AnimatorStateMachine>().Any(s => s.behaviours.Any(b => !b)))
                    throw new InvalidOperationException("元Controllerに読み込めないStateMachineBehaviourがあります。");
                if (internalAssets.Any(EditorUtility.IsDirty)) throw new InvalidOperationException("元controller内部assetに未保存編集があります。");
                foreach (var asset in internalAssets)
                    using (var so = new SerializedObject(asset))
                    {
                        var p = so.GetIterator();
                        while (p.Next(true))
                        {
                            if (p.propertyType != SerializedPropertyType.ObjectReference || !p.objectReferenceValue) continue;
                            var value = p.objectReferenceValue;
                            if (AssetDatabase.GetAssetPath(value) == originalPath) continue;
                            if (value is BlendTree tree && EditorUtility.CollectDependencies(new Object[] { tree }).OfType<AnimationClip>().Any(clips.Contains)
                                || value is RuntimeAnimatorController || value is AnimatorStateMachine || value is AnimatorState
                                || value is AnimatorTransitionBase || value is StateMachineBehaviour || value is Material)
                                throw new InvalidOperationException("別assetのAnimation graph/Material参照は未対応です: " + value.name);
                        }
                    }
                var paths = dependencies.Where(GraphObject).Select(AssetDatabase.GetAssetPath).Where(p => !string.IsNullOrEmpty(p)).Distinct().OrderBy(p => p);
                string revision = Digest(Encoding.UTF8.GetBytes(context + "\n" + string.Join("\n", paths.Select(p => p + ":" + Digest(File.ReadAllBytes(Absolute(p)))))));
                if (!AssetDatabase.IsValidFolder(folder)) { Directory.CreateDirectory(Absolute(folder)); AssetDatabase.Refresh(); }
                // Existing owned revisions are reused; Controller files are never overwritten.
                string key = Identity(source) + "_" + revision;
                string path = folder + "/SCAnim_" + Identity(source) + "_" + revision.Substring(0, 16) + ".controller";
                bool exists = File.Exists(Absolute(path));
                if (!exists && !AssetDatabase.CopyAsset(originalPath, path)) throw new IOException("Controller複製に失敗: " + path);
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                Own(path, key, !exists);
                if (!controller) throw new IOException("Controller複製を読み込めません: " + path);
                var copiedAssets = AssetDatabase.LoadAllAssetsAtPath(path).Where(o => o).ToArray();
                if (exists && copiedAssets.Any(EditorUtility.IsDirty)) throw new InvalidOperationException("専用Controller内部assetの未保存編集を上書きしません: " + path);
                var replacements = clips.ToDictionary(c => c, c => Clip(c, revision));
                foreach (var asset in copiedAssets)
                    using (var so = new SerializedObject(asset))
                    {
                        var p = so.GetIterator();
                        while (p.Next(true))
                        {
                            if (p.propertyType != SerializedPropertyType.ObjectReference || !p.objectReferenceValue) continue;
                            if (AssetDatabase.GetAssetPath(p.objectReferenceValue) == originalPath)
                                throw new InvalidOperationException("CopyAsset後に元controller内部参照が残っています。");
                            if (p.objectReferenceValue is AnimationClip clip && replacements.TryGetValue(clip, out var replacement))
                                p.objectReferenceValue = replacement;
                        }
                        so.ApplyModifiedPropertiesWithoutUndo();
                    }
                // All controller subassets share this asset file/GUID. Mark the
                // main object too, then save only this owned file, never SaveAssets.
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssetIfDirty(controller);
                controllers.Add(source, controller);
                return controller;
            }

            AnimationClip Clip(AnimationClip source, string revision)
            {
                string key = Identity(source) + "_" + revision;
                string path = folder + "/SCAnim_" + Identity(source) + "_" + revision.Substring(0, 16) + ".anim";
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (!clip && File.Exists(Absolute(path))) throw new IOException("別型の既存assetを上書きしません: " + path);
                if (clip)
                {
                    Own(path, key, false);
                    if (EditorUtility.IsDirty(clip)) throw new InvalidOperationException("専用Clipの未保存編集を上書きしません: " + path);
                    EditorUtility.CopySerialized(source, clip);
                }
                else { clip = Object.Instantiate(source); AssetDatabase.CreateAsset(clip, path); Own(path, key, true); }
                clip.name = source.name;
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(source))
                {
                    var match = Slot.Match(binding.propertyName);
                    if (!typeof(Renderer).IsAssignableFrom(binding.type) || !match.Success) continue;
                    var keys = AnimationUtility.GetObjectReferenceCurve(source, binding);
                    if (!keys.Any(k => k.value is Material)) continue;
                    var node = Find(root, binding.path); var renderer = node ? node.GetComponent<Renderer>() : null;
                    int slot = int.Parse(match.Groups[1].Value);
                    if (!(renderer is SkinnedMeshRenderer) || binding.type != typeof(SkinnedMeshRenderer)
                        || slot >= renderer.sharedMaterials.Length || !renderer.sharedMaterials[slot]
                        || !renderer.sharedMaterials[slot].HasProperty("_SCReady") || renderer.sharedMaterials[slot].GetFloat("_SCReady") <= 0)
                        throw new InvalidOperationException("準備済みSkinnedMeshRenderer slotへ解決できません: " + binding.path + " / " + slot);
                    for (int k = 0; k < keys.Length; k++) if (keys[k].value is Material original)
                    {
                        var converted = material(original, renderer, slot);
                        if (!converted || converted == original || !converted.HasProperty("_SCReady") || converted.GetFloat("_SCReady") <= 0)
                            throw new InvalidOperationException("専用準備済みMaterialへの差替えに失敗しました。");
                        string convertedPath = AssetDatabase.GetAssetPath(converted);
                        string outputPrefix = folder.Substring(0, folder.LastIndexOf('/')) + "/";
                        if (!convertedPath.StartsWith(outputPrefix, StringComparison.Ordinal))
                            throw new InvalidOperationException("変換素材はこの専用Generated出力へ保存してください。");
                        AssetDatabase.SaveAssetIfDirty(converted);
                        keys[k].value = converted;
                    }
                    AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
                }
                EditorUtility.SetDirty(clip);
                AssetDatabase.SaveAssetIfDirty(clip);
                return clip;
            }

            static void Own(string path, string key, bool created)
            {
                var importer = AssetImporter.GetAtPath(path); string tag = "ShapeChangeAnimations:" + key;
                if (!importer) throw new IOException("生成assetのImporterがありません: " + path);
                if (!created && importer.userData != tag) throw new IOException("所有を確認できない生成先を上書きしません: " + path);
                if (created) { importer.userData = tag; AssetDatabase.WriteImportSettingsIfDirty(path); }
            }
        }
    }
}
