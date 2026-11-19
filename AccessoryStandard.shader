Shader "ShapeChange/Accessory Standard Opaque"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _GlossMapScale ("Smoothness scale", Range(0,1)) = 1
        _Metallic ("Metallic", Range(0,1)) = 0
        _MetallicGlossMap ("Metallic", 2D) = "white" {}
        _BumpMap ("Normal", 2D) = "bump" {}
        _BumpScale ("Normal scale", Float) = 1
        _OcclusionMap ("Occlusion", 2D) = "white" {}
        _OcclusionStrength ("Occlusion strength", Range(0,1)) = 1
        _EmissionMap ("Emission", 2D) = "white" {}
        [HDR] _EmissionColor ("Emission color", Color) = (0,0,0,0)
        [Toggle] _SCEnabled ("変形を有効にする", Float) = 1
        _SCAmount ("変形量", Range(0,1)) = 0
        _SCMode ("形状", Float) = 0
        [Enum(Keep,0,Fold,1,Hide,2)] _SCArms ("腕", Float) = 1
        [Enum(Keep,0,Fold,1,Hide,2)] _SCLegs ("脚", Float) = 1
        _SCArmMorph ("腕の変形・収納モーフ", Range(0,1)) = 0.35
        _SCLegMorph ("脚の変形・収納モーフ", Range(0,1)) = 0.35
        _SCSize ("形状の大きさ", Range(0.2,2)) = 1
        _SCBoxAspect ("直方体の幅・高さ・奥行倍率", Vector) = (1,1,1,0)
        _SCInflation ("膨体の横・奥行の膨らみ", Range(0.5,2.5)) = 1
        _SCPressSpread ("頭上から圧縮：横へ広がる量", Range(0,2)) = 0.5
        _SCPanelWidth ("長方形の幅 / 身長比", Range(0.1,1.5)) = 0.7
        _SCClothBend ("反物の共通たわみ / 身長比", Range(0,0.2)) = 0.06
        [HideInInspector] _SCSheetTex ("Front / back panel appearance", 2D) = "white" {}
        [HideInInspector] _SCCubeTex ("Cube face appearance", 2D) = "white" {}
        [HideInInspector] _SCCubeReady ("Cube face appearance ready", Float) = 0
        _SCThickness ("平面・コインの厚み / 身長比", Range(0.002,0.2)) = 0.025
        _SCLayer ("衣装の重なり保持", Range(0.002,0.15)) = 0.025
        _SCNormalBlend ("変形法線", Range(0,1)) = 1
        [Toggle] _SCSmoothEnvelope ("凹凸を減らして外形へなじませる", Float) = 1
        [Toggle] _SCPreserveSurface ("顔の縦横比・前後中央の形を保持", Float) = 1
        _SCSurfaceFollow ("等倍保持時・胴体の形状への追従", Range(0,1)) = 0.12
        _SCFaceCoverage ("顔の占有率 / 正面投影の目安", Range(0.05,0.7)) = 0.4
        _SCFaceStart ("顔の下端 / 形状の高さ比", Range(0.3,0.85)) = 0.44
        _SCBodyBase ("収納時の胴体下端 / 身長比", Range(0,0.7)) = 0.45
        [Toggle] _SCUseProxy ("閉じた補助メッシュへ切替", Float) = 0
        [HideInInspector] _SCProxy ("Closed surface", Float) = 0
        [HideInInspector] _SCAccessory ("Rigid attachment / keep skinning", Float) = 0
        [HideInInspector] _SCAppendage ("Integrate ears / tail into shape", Float) = 0
        [HideInInspector] _SCVertexData ("Attachment rest frames", 2D) = "white" {}
        [HideInInspector] _SCPanelProfile ("Shared panel row bounds", 2D) = "black" {}
        [HideInInspector] _SCAttachmentDataReady ("Attachment rest frames prepared", Float) = 0
        [Toggle] _SCUpright ("直立配置（オフ＝従来の前寄せ）", Float) = 1
        [Toggle] _SCAccessoryAutoScale ("アクセサリーを装着先の倍率へ合わせる", Float) = 1
        _SCAccessoryScale ("アクセサリー倍率の補正", Range(0.1,3)) = 1
        [HideInInspector] _SCSeated ("Seated rig pose", Float) = 0
        [HideInInspector] _SCRigReady ("Limb anchors calibrated", Float) = 0
        [HideInInspector] _SCHeadFront ("Head front from center", Float) = 0.09
        [HideInInspector] _SCPanelBottom ("Torso panel bottom", Float) = 0.25
        [HideInInspector] _SCArmL ("Left upper arm", Vector) = (0.12,0.76,0,0)
        [HideInInspector] _SCArmR ("Right upper arm", Vector) = (-0.12,0.76,0,0)
        [HideInInspector] _SCLegL ("Left upper leg", Vector) = (0.06,0.55,0,0)
        [HideInInspector] _SCLegR ("Right upper leg", Vector) = (-0.06,0.55,0,0)
        [HideInInspector] _SCReady ("Calibrated", Float) = 0
        [HideInInspector] _SCFrame ("Root center XYZ / height", Vector) = (0,0,0,1.5)
        [HideInInspector] _SCMeasure ("Neck / head size / half width / half depth", Vector) = (0.8,0.2,0.15,0.08)
        [HideInInspector] _SCHead ("Head center XY / half width / center Z", Vector) = (0,0.9,0.075,0)
        [HideInInspector] _SCFaceSize ("Measured face width / height", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert addshadow
        #pragma target 3.5
        #pragma shader_feature_local _NORMALMAP
        #pragma shader_feature_local _METALLICGLOSSMAP
        #pragma shader_feature_local _EMISSION
        #pragma multi_compile_instancing
        #include "UnityCG.cginc"
        #include "UnityStandardUtils.cginc"
        #include "ShapeChange.hlsl"
        sampler2D _MainTex, _BumpMap, _MetallicGlossMap, _OcclusionMap, _EmissionMap;
        #define SC_STANDARD_MAIN(uv) tex2D(_MainTex,uv)
        #define SC_STANDARD_MAIN_GRAD(uv,dx,dy) tex2Dgrad(_MainTex,uv,dx,dy)
        fixed4 _Color;
        half _Glossiness, _GlossMapScale, _Metallic, _BumpScale, _OcclusionStrength;
        half4 _EmissionColor;
        struct appdata_sc
        {
            float4 vertex : POSITION;
            float3 normal : NORMAL;
            float4 tangent : TANGENT;
            float4 texcoord : TEXCOORD0;
            float4 texcoord1 : TEXCOORD1;
            float4 texcoord2 : TEXCOORD2;
            float2 texcoord4 : TEXCOORD4;
            float2 texcoord5 : TEXCOORD5;
            float2 texcoord6 : TEXCOORD6;
            float2 texcoord7 : TEXCOORD7;
            uint vertexID : SV_VertexID;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Input { float2 uv_MainTex; float4 scData; float4 screenPos; };
        void vert(inout appdata_sc v,out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input,o);
            SCDeformPrepared(v.vertex,v.normal,v.tangent,v.texcoord4,v.texcoord5,v.texcoord6,v.texcoord7,v.vertexID);
            float3 p=float3(v.texcoord4,v.texcoord5.x);
            if(_SCProxy>0.5)v.texcoord.xy=SCAtlasUV(v.texcoord.xy,p);
            o.scData=SCSurfaceData(p,float3(v.texcoord5.y,v.texcoord6));
        }
        void surf(Input input,inout SurfaceOutputStandard o)
        {
            SCClip(input.scData.xy,input.screenPos.xy/max(input.screenPos.w,0.00001)*_ScreenParams.xy);
            fixed4 albedo=tex2D(_MainTex,input.uv_MainTex);
            SC_APPLY_PROXY_COLOR(albedo,input.uv_MainTex,input.scData.zw,SC_STANDARD_MAIN,SC_STANDARD_MAIN_GRAD)
            albedo*=_Color;
            o.Albedo=albedo.rgb;o.Alpha=albedo.a;
            #if defined(_METALLICGLOSSMAP)
                half4 metal=tex2D(_MetallicGlossMap,input.uv_MainTex);
                o.Metallic=metal.r;o.Smoothness=metal.a*_GlossMapScale;
            #else
                o.Metallic=_Metallic;o.Smoothness=_Glossiness;
            #endif
            #if defined(_NORMALMAP)
                o.Normal=UnpackScaleNormal(tex2D(_BumpMap,input.uv_MainTex),_BumpScale);
            #endif
            o.Occlusion=lerp(1.0,tex2D(_OcclusionMap,input.uv_MainTex).g,_OcclusionStrength);
            #if defined(_EMISSION)
                o.Emission=tex2D(_EmissionMap,input.uv_MainTex).rgb*_EmissionColor.rgb;
            #endif
        }
        ENDCG
    }
    CustomEditor "ShapeChange.ShapeChangeInspector"
    Fallback Off
}
