Shader "Hidden/ShapeChange/Validation"
{
    Properties
    {
        _MainTex ("Test samples", 2D) = "black" {}
        _SCPanelProfile ("Panel row bounds", 2D) = "black" {}
        _SCCubeTex ("Cube test tiles", 2D) = "white" {}
    }
    SubShader
    {
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "../ShapeChange.hlsl"
            sampler2D _MainTex;
            sampler2D _ProbeMasks;
            float _ProbeNormal, _ProbeActual, _ProbeFade, _ProbeSkinned;
            float _ProbeEnvelope, _ProbeReference, _ProbeContact, _ProbeShape;
            float _ProbePanelInverse, _ProbeCubeAppearance, _ProbeCylinderSideUV;
            float4 _ProbeAnchor, _ProbeAnchorMasks;
            float4 _ProbeAnchorNormal;
            float4x4 _ProbePose;
            float _ProbePrepared, _ProbeVertexIndex;
            float4 _ProbeRestNormal, _ProbeRestTangent;
            float4 frag(v2f_img input) : SV_Target
            {
                float4 sample = tex2D(_MainTex,input.uv);
                float3 p=sample.xyz;
                if(_ProbeCylinderSideUV>0.5)
                    return float4(SCCylinderSideUV(SCAtlasUV(p.xy,0)),0,1);
                if(_ProbeCubeAppearance>0.5)
                    return SCCubeAppearance()?SCCubeColor(p.xy,0.7,float4(0,0,0,1)):float4(0,0,0,1);
                if(_ProbePanelInverse>0.5)
                {
                    float3 target=SCAddedBasePosition(p,1),layout=SCPanelLayout();
                    float y=SCPanelSourceHeight(target.y,layout);float2 section=SCPanelSection(y);
                    return float4(target.x/(max(_SCPanelWidth,0.1)*max(_SCSize,0.05))*section.y+section.x,y,target.z,1);
                }
                if(_ProbeShape>0.5)return float4(SCHalfSize(),SCGetShape().headScale);
                if(_ProbeReference>0.5)return float4(_SCMode>=13?SCAddedPosition(p,saturate(_SCAmount)):SCPosition(p,_ProbeAnchorMasks.xyz,SCGetShape(),saturate(_SCAmount)),1);
                if(_ProbeEnvelope>0.5)
                {
                    SCShape shape=SCGetShape();
                    return float4(SCEnvelope(p,shape,saturate(_SCAmount)),shape.headScale);
                }
                float4 vertex=float4(p*_SCFrame.w+_SCFrame.xyz,1);
                float3 normal=float3(0,0,1);
                float4 tangent=float4(1,0,0,1);
                float3 masks=sample.w<0.5?float3(0,0,0):sample.w<1.5?float3(1,0,0):sample.w<2.5?float3(0,1,0):float3(0,0,1);
                if(_ProbeActual>0.5) masks=tex2D(_ProbeMasks,input.uv).xyz;
                if(_ProbeContact>0.5)masks=_ProbeAnchorMasks.xyz;
                if(_ProbeContact>0.5 || _SCAccessory>0.5)normal=SCDecodeNormal(_ProbeAnchorNormal.xy);
                tangent.xyz=SCNormalize(tangent.xyz-normal*dot(normal,tangent.xyz),float3(0,1,0));
                float3 restNormal=normal,restTangent=tangent.xyz;
                if(_ProbePrepared>0.5)
                {
                    restNormal=_ProbeRestNormal.xyz;restTangent=_ProbeRestTangent.xyz;
                    normal=restNormal;tangent.xyz=restTangent;
                }
                if(_SCAccessory>0.5 || _ProbeSkinned>0.5)
                {
                    vertex=mul(_ProbePose,vertex);
                    normal=mul((float3x3)_ProbePose,normal);
                    tangent.xyz=mul((float3x3)_ProbePose,tangent.xyz);
                }
                if(_SCAccessory>0.5){p=_ProbeAnchor.xyz;masks=_ProbeAnchorMasks.xyz;}
                if(_ProbePrepared>1.5)SCDeformPrepared(vertex,normal,tangent,p.xy,float2(p.z,masks.x),masks.yz,_ProbeAnchorNormal.xy,(uint)_ProbeVertexIndex);
                else SCDeform(vertex,normal,tangent,p.xy,float2(p.z,masks.x),masks.yz,(_SCAccessory>0.5 || _ProbeContact>0.5)?_ProbeAnchorNormal.xy:float2(0,0),sample.xyz,restNormal,restTangent,1);
                float head=_SCPreserveSurface>0.5?SCHeadWeight(p,masks):0;
                SCShape shape=SCGetShape();
                float protectedArea=_SCMode>=9?0:head>=1.0 ? -shape.headScale : head>0 ? 0 : SCPanelWeight(p,masks)>=1.0 && SCLimbWeight(masks)<0.00001 ? shape.panelScale : 0;
                float alpha=_SCAccessory>0.5?lerp(1.0,SCAttachmentScale(p,masks,shape),saturate(_SCAmount)):protectedArea;
                return float4(_ProbeNormal>1.5?tangent.xyz:_ProbeNormal>0.5?normal:vertex.xyz,_ProbeFade>0.5?SCProxyFade(p,masks):alpha);
            }
            ENDCG
        }
    }
}
