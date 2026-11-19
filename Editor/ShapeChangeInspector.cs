using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ShapeChange
{
    public class ShapeChangeInspector : ShaderGUI
    {
        ShaderGUI lilToonInspector;

        public override void OnGUI(MaterialEditor editor, MaterialProperty[] properties)
        {
            EditorGUILayout.LabelField("ShapeChange / 形状変化", EditorStyles.boldLabel);
            if(FindProperty("_SCAccessory",properties).floatValue>0.5f)
                EditorGUILayout.HelpBox("アクセサリー：縦横比を保って装着先の倍率に合わせ、元のリギングに追従します。",MessageType.Info);
            var ready = FindProperty("_SCReady", properties);
            var enabled = FindProperty("_SCEnabled", properties);
            if (ready.floatValue < 0.5f)
                EditorGUILayout.HelpBox("Tools > ShapeChange > Setup Avatar で体格・部位情報を焼き込んでください。", MessageType.Info);
            foreach (var property in properties)
                if (property.name.StartsWith("_SC",StringComparison.Ordinal) && property.name!="_SCEnabled"
                    && (property.flags & MaterialProperty.PropFlags.HideInInspector) == 0)
                {
                    if(property.name=="_SCAmount")
                    {
                        EditorGUI.showMixedValue=property.hasMixedValue || enabled.hasMixedValue;
                        EditorGUI.BeginChangeCheck();
                        bool on=EditorGUILayout.Toggle("変形（On / Off）",enabled.floatValue>0.5f && property.floatValue>=0.5f);
                        if(EditorGUI.EndChangeCheck())
                        {
                            editor.RegisterPropertyChangeUndo("ShapeChange On / Off");
                            enabled.floatValue=1;
                            property.floatValue=on?1:0;
                        }
                        EditorGUI.showMixedValue=false;
                    }
                    else if(property.name=="_SCMode")
                    {
                        EditorGUI.showMixedValue=property.hasMixedValue;
                        EditorGUI.BeginChangeCheck();
                        int value=EditorGUILayout.Popup("形状",Mathf.Clamp(Mathf.RoundToInt(property.floatValue),0,ShapeChangeSetup.Modes.Length-1),ShapeChangeSetup.Modes);
                        if(EditorGUI.EndChangeCheck()) { editor.RegisterPropertyChangeUndo("Shape");property.floatValue=value; }
                        EditorGUI.showMixedValue=false;
                    }
                    else editor.ShaderProperty(property, property.displayName);
                }
            EditorGUILayout.Space();
            if (editor.target is Material material && material.shader.name.Contains("lilToon"))
            {
                var inspectorType = Type.GetType("lilToon.lilToonInspector, lilToon.Editor");
                if (inspectorType != null)
                {
                    lilToonInspector ??= (ShaderGUI)Activator.CreateInstance(inspectorType);
                    lilToonInspector.OnGUI(editor, properties);
                    return;
                }
            }
            editor.PropertiesDefaultGUI(properties.Where(p => !p.name.StartsWith("_SC", StringComparison.Ordinal)).ToArray());
        }
    }
}
