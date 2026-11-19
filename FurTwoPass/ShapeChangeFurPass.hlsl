#ifndef SHAPE_CHANGE_FUR_PASS_INCLUDED
#define SHAPE_CHANGE_FUR_PASS_INCLUDED

#if defined(LIL_ONEPASS_FUR) || defined(Append) || defined(geom) || defined(LIL_VERTEX_FUR_INCLUDED) || defined(LIL_V2F_OUT_BASE) || defined(LIL_CUSTOM_V2G_MEMBER)
    #error ShapeChange FurTwoPass requires the lilToon 2.4.1 TwoPass geometry include layout.
#endif

#define LIL_CUSTOM_V2G_MEMBER(id0,id1,id2,id3,id4,id5,id6,id7) float4 scData : TEXCOORD##id0;
// Load the stock fragment and structures before adding the geometry data bridge.
#define LIL_VERTEX_FUR_INCLUDED
#include "Packages/jp.lilxyzw.liltoon/Shader/Includes/lil_pass_forward_fur.hlsl"
#undef LIL_VERTEX_FUR_INCLUDED
#undef LIL_CUSTOM_V2G_MEMBER

v2f SCCopyFurData(v2f output, v2g input[3], float3 factor)
{
    output.scData = input[0].scData*factor.x + input[1].scData*factor.y + input[2].scData*factor.z;
    return output;
}

// shortcut: lilToon 2.4.1 TwoPass fin Append sites, revalidate this bridge when upgrading lilToon.
#define LIL_V2F_OUT_BASE output
#define Append(value) Append(SCCopyFurData(value,input,factor))
#define geom SCFurGeometry
float4 SCPlaneFurVector()
{
    float amount=_SCMode>=9?saturate(_SCAmount)*step(0.5,_SCEnabled)*step(0.5,_SCReady):0;
    return float4(_FurVector.xyz,_FurVector.w*lerp(1,0.001,amount));
}
// Fur is emitted after vertex projection; compress its fins as well as the base mesh.
#define _FurVector SCPlaneFurVector()
#include "Packages/jp.lilxyzw.liltoon/Shader/Includes/lil_common_vert_fur.hlsl"
#undef _FurVector
#undef geom
#undef Append
#undef LIL_V2F_OUT_BASE

// TwoPass emits at most 26 vertices; the stock reserve exceeds DX11's component budget with scData.
[maxvertexcount(26)]
void geom(triangle v2g input[3], inout TriangleStream<v2f> outStream)
{
    SCFurGeometry(input, outStream);
}

#endif
