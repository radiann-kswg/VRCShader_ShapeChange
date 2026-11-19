#ifndef SHAPE_CHANGE_INCLUDED
#define SHAPE_CHANGE_INCLUDED

#define LIL_REQUIRE_APP_TEXCOORD4
#define LIL_REQUIRE_APP_TEXCOORD5
#define LIL_REQUIRE_APP_TEXCOORD6
#define LIL_REQUIRE_APP_TEXCOORD7
#define LIL_REQUIRE_APP_NORMAL
#define LIL_REQUIRE_APP_TANGENT
#define LIL_REQUIRE_APP_VERTEXID

float _SCEnabled, _SCAmount, _SCMode, _SCArms, _SCLegs;
float _SCArmMorph, _SCLegMorph;
float _SCSize, _SCThickness, _SCLayer, _SCNormalBlend, _SCReady;
float _SCProxy, _SCUseProxy;
float _SCPreserveSurface, _SCSurfaceFollow;
float _SCFaceCoverage;
float _SCSmoothEnvelope;
float _SCAccessory;
float _SCAppendage;
float _SCUpright, _SCSeated, _SCRigReady, _SCHeadFront;
float _SCAccessoryAutoScale, _SCAccessoryScale;
float4 _SCArmL, _SCArmR, _SCLegL, _SCLegR;
float _SCFaceStart, _SCBodyBase, _SCPanelBottom;
float4 _SCFrame, _SCMeasure, _SCHead, _SCFaceSize;
float4 _SCBoxAspect;
float _SCInflation, _SCPressSpread, _SCPanelWidth, _SCClothBend;
sampler2D _SCSheetTex;
sampler2D _SCCubeTex;
float4 _SCCubeTex_TexelSize;
float _SCCubeReady;
sampler2D _SCPanelProfile;
sampler2D _SCVertexData;
float4 _SCVertexData_TexelSize;
float _SCAttachmentDataReady;

bool SCOriginalFlat(){return _SCMode>=9 && _SCMode<=12;}
bool SCPanel(){return _SCMode==14 || _SCMode==15;}
bool SCVolumeSurface(){return _SCMode<=8 && _SCUpright>0.5 && _SCSmoothEnvelope>0.5 && _SCPreserveSurface>0.5;}
bool SCBoxFace(){return _SCMode<=1 && SCVolumeSurface();}
bool SCCapProjection(){return (_SCMode==6 || _SCMode==8) && SCVolumeSurface();}
bool SCCubeAppearance(){return _SCProxy>0.5 && _SCCubeReady>0.5 && SCVolumeSurface() && (_SCMode<=5 || _SCMode==8);}
float SCProxyStart(){return SCPanel()?0.35:(_SCMode<=2 || _SCMode==6 || _SCMode==7)?0.45:0.65;}
float SCVolumeProxyGrowthEnd(){return min(SCProxyStart()+0.25,0.82);}
float SCActiveAmount(){return saturate(_SCAmount)*step(0.5,_SCEnabled)*step(0.5,_SCReady);}

// A bounded, strictly increasing depth map levels relief without swapping clothing layers.
float SCPlaneDepth(float z,float thickness)
{
    float soft=max(_SCMeasure.w,0.025)*clamp(_SCLayer*4,0.08,0.4);
    return thickness*0.5*z/(abs(z)+soft);
}

float SCPlaneDepthScale(float z,float thickness,float amount)
{
    float soft=max(_SCMeasure.w,0.025)*clamp(_SCLayer*4,0.08,0.4);
    return lerp(1,thickness*0.5*soft/pow(abs(z)+soft,2),amount);
}

float3 SCPanelLayout()
{
    float height=(_SCMode==15?1.4:1)*max(_SCSize,0.05);
    float cut=clamp((_SCFaceSize.w>_SCFaceSize.z+0.03?_SCFaceSize.z:_SCMeasure.x)-0.025,0.35,0.9);
    float faceHeight=_SCFaceSize.w>_SCFaceSize.z+0.03?_SCFaceSize.w-_SCFaceSize.z:max(_SCMeasure.y,0.05);
    float scale=min(height*0.3/faceHeight,height*0.35/(1.05-cut));
    return float3(cut,height-(1.05-cut)*scale,scale);
}

float SCPanelHeight(float y,float3 layout)
{
    float k=layout.z*layout.x/layout.y,t=y/layout.x;
    return y>=layout.x?layout.y+(y-layout.x)*layout.z:
        y<0?y*layout.y/(layout.x*k):layout.y*t/(k+(1-k)*t);
}

float SCPanelSourceHeight(float y,float3 layout)
{
    float k=layout.z*layout.x/layout.y;
    return y>=layout.y?layout.x+(y-layout.y)/layout.z:
        y<0?y*layout.x*k/layout.y:layout.x*y*k/(layout.y-(1-k)*y);
}

float2 SCPanelSection(float y)
{
    float4 row=tex2Dlod(_SCPanelProfile,float4(0.5,saturate((y+0.05)/1.1),0,0));
    float center=row.b>0.5?(row.r+row.g)*0.5:0;
    // A minimum source span keeps narrow silhouette tips from overstretching.
    float span=row.b>0.5?max(row.g-row.r,max(_SCMeasure.z,0.075)):max(_SCMeasure.z*2,0.05);
    return float2(center,span);
}

// New modes act on the current skinned position for every renderer. Coincident
// skin, clothing and attachment points therefore follow exactly the same field.
float3 SCAddedBasePosition(float3 p,float amount)
{
    float size=max(_SCSize,0.05),thickness=max(_SCThickness,0.004);
    if(_SCMode==13)
    {
        // Positive shears turn the face toward the pressed sheet; the floor stays fixed.
        float spread=1+max(_SCPressSpread,0)*amount*amount*smoothstep(0,1,p.y);
        float headY=max(_SCHead.y,0.5);
        float headSpread=1+max(_SCPressSpread,0)*amount*amount*smoothstep(0,1,headY);
        float start=min(max(_SCMeasure.x-0.25,0.2),headY-0.12);
        float bend=(p.y-start)*smoothstep(start,start+0.12,p.y);
        p.xz*=spread;
        p.z-=amount*bend*spread;
        p.y*=exp(clamp(amount*(p.z/(headY*headSpread)-1+(2-start/headY)*amount),-8,1));
        // The final press is strictly increasing; no vertex snaps onto the floor.
        float h=lerp(1.0,thickness,amount),front=max((1-amount)*0.65,0.0001);
        float y=h*p.y+(1-h)*front*(1-exp(-max(p.y,0)/front));
        // Round the pressed footprint without changing the height or exchanging layers.
        float roundness=smoothstep(0.55,1,amount);
        float2 center=float2(0,-headY*0.38),sourceHalf=float2(0.26,headY*0.62);
        float2 q=(p.xz-center)/sourceHalf,origin=-center/sourceHalf;
        p.xz=(q/(1+0.25*roundness*length(q))-origin/(1+0.25*roundness*length(origin)))
            *lerp(sourceHalf,float2(0.46,headY*0.52),roundness);
        return float3(p.x,y,p.z)*lerp(1.0,size,amount);
    }
    if(_SCProxy<0.5)
    {
        float3 layout=SCPanelLayout();float2 section=SCPanelSection(p.y);
        float3 target=float3((p.x-section.x)*max(_SCPanelWidth,0.1)*size/section.y,
            SCPanelHeight(p.y,layout),SCPlaneDepth(p.z,thickness*size));
        return lerp(p,target,amount);
    }
    float3 sourceHalf=float3(max(_SCMeasure.z,0.025),0.5,max(_SCMeasure.w,0.025));
    float3 q=(p-float3(0,0.5,0))/sourceHalf;
    float radius=length(q);
    float3 direction=radius>0.000001?q/radius:float3(0,0,0);
    float maximum=max(max(abs(direction.x),abs(direction.y)),abs(direction.z));
    float3 power=pow(abs(direction)/max(maximum,0.0001),32.0);
    float boxNorm=maximum*pow(max(power.x+power.y+power.z,0.000001),1.0/32.0);
    float layer=clamp(_SCLayer*4,0.08,0.4);
    float relief=_SCProxy>0.5?radius:radius/(layer+(1-layer)*radius);
    float3 halfSize=float3(max(_SCPanelWidth,0.1)*0.5,_SCMode==15?0.7:0.5,thickness*0.5)*size;
    // Interpolate positive radial scale and dimensions. Unlike endpoint lerp,
    // this cannot turn an expanding rectangle inside out at intermediate times.
    float3 center=float3(0,lerp(0.5,halfSize.y,amount),0);
    return center+direction*lerp(radius,relief/max(boxNorm,0.0001),amount)*lerp(sourceHalf,halfSize,amount)
        *smoothstep(SCProxyStart(),0.67,amount);
}

float SCClothWave(float y,float amount)
{
    return max(_SCClothBend,0)*max(_SCSize,0.05)*amount*sin(y/(1.4*max(_SCSize,0.05))*6.283185307);
}

float3 SCAddedPosition(float3 p,float amount)
{
    float3 result=SCAddedBasePosition(p,amount);
    // A common shear bends both sides without changing depth order. Its
    // determinant is one; use its exact derivative for thin-sheet normals.
    if(_SCMode==15)result.xz+=float2(0.35,1)*SCClothWave(result.y,amount);
    return result;
}

float SCArmKeep(){return _SCMode==3?(_SCArms<1.5?1:0):(_SCArms<0.5?1-saturate(_SCArmMorph):0);}
float SCLegKeep(){return _SCMode==3?(_SCLegs<1.5?1:0):(_SCLegs<0.5?1-saturate(_SCLegMorph):0);}
float SCLimbWeight(float3 masks)
{
    return max(smoothstep(0.05,0.95,masks.y)*SCArmKeep(),smoothstep(0.05,0.95,masks.z)*SCLegKeep());
}
float SCLimbScale(float3 masks,float3 halfSize)
{
    float morph=saturate(masks.y>=masks.z?_SCArmMorph:_SCLegMorph);
    if(_SCMode==3)
    {
        float control=masks.y>=masks.z?_SCArms:_SCLegs;
        return lerp(1,max(_SCSize,0.05)*0.25,control<0.5?morph:1);
    }
    float size=clamp(sqrt(halfSize.x*halfSize.y/max(_SCMeasure.z*(_SCMeasure.x-_SCPanelBottom)*0.5,0.001)),1,2);
    // Expand the limb while absorbing it, then converge exactly to the body.
    return lerp(1,size,4*morph*(1-morph));
}

float3 SCLimbStretch(float3 masks,float3 halfSize)
{
    float axial=SCLimbScale(masks,halfSize);
    if(_SCMode!=3)return float3(axial,axial,axial);
    bool arm=masks.y>=masks.z;
    float morph=arm?_SCArmMorph:_SCLegMorph,control=arm?_SCArms:_SCLegs;
    float width=lerp(1,max(_SCSize,0.05)*0.7,control<0.5?saturate(morph):1);
    return arm?float3(axial,width,width):float3(width,axial,width);
}

float3 SCNormalize(float3 p, float3 fallback)
{
    float d = dot(p,p);
    return d > 1e-12 ? p * rsqrt(d) : fallback;
}

float3 SCDecodeNormal(float2 e)
{
    float3 n = float3(e, 1.0 - abs(e.x) - abs(e.y));
    float t = saturate(-n.z);
    n.xy += float2(n.x >= 0 ? -t : t, n.y >= 0 ? -t : t);
    return SCNormalize(n, float3(0,1,0));
}

// The centre of each front/back panel undergoes a translation only. A spatial
// mask shared across renderers avoids pulling UV seams/material boundaries apart.
float SCPanelWeight(float3 p, float3 masks)
{
    float width = max(_SCMeasure.z*1.3, _SCMeasure.y*0.45);
    float depth = max(_SCMeasure.w,0.025);
    float panel = (1.0-smoothstep(width,width*1.8,abs(p.x)))
        * smoothstep(depth*0.08,depth*0.3,abs(p.z));
    float limb = max((1-SCArmKeep())*masks.y,(1-SCLegKeep())*masks.z);
    return panel*(1.0-smoothstep(0.05,0.8,limb))*(1.0-smoothstep(0.1,0.95,masks.x))*saturate(_SCPreserveSurface)*(1.0-saturate(_SCSmoothEnvelope));
}

float3 SCRotateFace(float3 p)
{
    return (_SCMode==6 || _SCMode==9) ? float3(p.x,p.z,-p.y) : p;
}

// A sheet is the original silhouette with positive thickness. A single affine
// map preserves the order of all skin/clothing layers and shared seams.
float3 SCFlatScale()
{
    float scale=max(_SCSize,0.05);
    return float3((_SCMode==11?0.52:_SCMode==12?1.15:1)*scale,scale,
        max(_SCThickness,0.004)*scale/max(2*_SCMeasure.w,0.08));
}

float3 SCFlatRotate(float3 p,float amount)
{
    float s,c;sincos(_SCMode==9?amount*1.570796327:0,s,c);
    return float3(p.x,c*p.y+s*p.z,-s*p.y+c*p.z);
}

float3 SCFlatPosition(float3 p,float amount)
{
    float3 center=float3(0,0.5,0);
    float3 destination=float3(0,_SCMode==9?max(_SCThickness,0.004)*max(_SCSize,0.05)*0.5:max(_SCSize,0.05)*0.5,0);
    float3 result=(p-center)*lerp(1.0,SCFlatScale(),amount);
    result.z=lerp(p.z,SCPlaneDepth(p.z,max(_SCThickness,0.004)*max(_SCSize,0.05)),amount);
    return SCFlatRotate(result,amount)+lerp(center,destination,amount);
}

float3 SCHalfSize()
{
    int mode=(int)round(clamp(_SCMode,0.0,12.0));
    float thickness=max(_SCThickness,0.002),size=max(_SCSize,0.05);
    float3 halfSize=float3(0.35,0.35,0.35)*size;
    // Measured anatomy normalizes the source; destination proportions use the shared nominal rig.
    if(mode==1) halfSize=float3(0.24,0.11,0.43)*size;
    if(mode==2) halfSize=0.28*size;
    if(mode==3) halfSize=0.5*size;
    if(mode==5) halfSize=0.25*size;
    if(mode==6 || mode==7) halfSize=float3(0.28,0.5,0.28)*size;
    if(mode==8) halfSize=float3(0.4,0.4,thickness*0.5)*size;
    if(mode==9 || mode==10) halfSize=float3(0.35,0.5,thickness*0.5)*size;
    if(mode==11) halfSize=float3(0.16,0.7,thickness*0.5)*size;
    if(mode==12) halfSize=float3(0.32,0.5,thickness*0.5)*size;
    if(mode==1)halfSize*=max(_SCBoxAspect.xyz,float3(0.1,0.1,0.1));
    if(mode==3)halfSize.xz*=max(_SCInflation,0.1);
    return halfSize;
}

float SCBodyLift(float3 halfSize)
{
    if(_SCRigReady<0.5 || SCLegKeep()<=0) return 0;
    if(_SCMode==3)return lerp(max((_SCLegL.y+_SCLegR.y)*0.5*SCLimbScale(float3(0,0,1),halfSize)-halfSize.y*0.08,0),0.03,saturate(_SCSeated));
    return lerp(max((_SCLegL.y+_SCLegR.y)*0.5-0.04,0)*SCLimbScale(float3(0,0,1),halfSize),0.03,saturate(_SCSeated))*SCLegKeep();
}

float3 SCLimbTarget(float3 p,float3 masks,float3 halfSize)
{
    bool arm=masks.y>=masks.z;
    float3 joint=arm ? (p.x*_SCArmL.x>=0?_SCArmL.xyz:_SCArmR.xyz) : (p.x*_SCLegL.x>=0?_SCLegL.xyz:_SCLegR.xyz);
    float side=joint.x>=0?1.0:-1.0;
    float bodyTop=halfSize.y*1.38;
    float3 destination=arm ? float3(side*halfSize.x*0.92,bodyTop,halfSize.z*0.8)
        : float3(side*min(halfSize.x*0.42,0.15),lerp(0.04,halfSize.y*0.38,saturate(_SCSeated)),halfSize.z*0.65*saturate(_SCSeated));
    if(_SCMode==3)
        destination=arm ? float3(side*halfSize.x*0.91,halfSize.y*1.45,halfSize.z*0.43)
            : float3(side*halfSize.x*0.35,halfSize.y*lerp(0.08,0.38,saturate(_SCSeated)),halfSize.z*lerp(0.18,0.65,saturate(_SCSeated)));
    return (p-joint)*SCLimbStretch(masks,halfSize)+destination;
}

float SCHeadCut()
{
    float chin=_SCFaceSize.w>_SCFaceSize.z+0.03?_SCFaceSize.z:_SCMeasure.x;
    return min(_SCMeasure.x-0.025,chin-0.015);
}

float SCHeadWeight(float3 p,float3 masks)
{
    if(_SCProxy<0.5 && _SCSmoothEnvelope>0.5 && _SCPreserveSurface>0.5 && (_SCMode<=5 || _SCMode==7))
    {
        float chin=_SCFaceSize.w>_SCFaceSize.z+0.03?_SCFaceSize.z:_SCMeasure.x;
        // A common height interval leaves every horizontal section ordered, including enlarged heads.
        return smoothstep(min(_SCMeasure.x-0.08,chin-0.07),SCHeadCut(),p.y)*(1-smoothstep(0.05,0.8,max(masks.y,masks.z)));
    }
    // Keep the face/eyes rigid together, blend long hair and the neck below it.
    float transition=lerp(0.05,0.13,saturate(_SCUpright));
    float start=_SCMeasure.x-transition;
    // A partially projected chin falls through the top of the cylinder as its shell takes over.
    float end=_SCMode==6 && SCCapProjection()?max(start+0.001,SCHeadCut()):_SCMeasure.x+0.01;
    float head=smoothstep(0.05,0.95,masks.x)*smoothstep(start,end,p.y);
    // Carry the collar with the intact head, irrespective of its cloth bone
    // weights. A shared spatial patch keeps skin/cloth material seams together.
    if(_SCMode<=5 && _SCUpright>0.5)
    {
        float chin=_SCFaceSize.w>_SCFaceSize.z+0.03?_SCFaceSize.z:_SCMeasure.x;
        float2 neckOffset=(p.xz-float2(_SCHead.x,_SCHead.w))/float2(max(_SCHead.z,0.04)*1.35,max(_SCHeadFront,0.04)*1.2);
        float collar=(1-smoothstep(1.25,1.75,length(neckOffset)))
            *smoothstep(_SCMeasure.x-0.15,_SCMeasure.x-0.065,p.y)
            *(1-smoothstep(chin,chin+0.025,p.y));
        head=max(head,collar*(1-smoothstep(0.05,0.8,max(masks.y,masks.z))));
    }
    return head;
}

float SCHeadHeight(){return _SCFaceSize.w>_SCFaceSize.z+0.03? _SCFaceSize.w-_SCFaceSize.z:max(_SCMeasure.y,0.05);}

// Compute shape-wide quantities once, also when evaluating normal derivatives.
struct SCShape
{
    float3 halfSize;
    float3 bodyHalfSize;
    float headScale;
    float bodyTop;
    float panelScale;
};

SCShape SCGetShape()
{
    SCShape shape;
    float3 halfSize=SCHalfSize();
    float headHeight=SCHeadHeight(),headWidth=max(_SCHead.z*2.0,headHeight*0.65);
    float area=4.0*halfSize.x*halfSize.y;
    if(_SCMode==3 || _SCMode==4 || _SCMode==5 || _SCMode==8) area*=0.78539816;
    if(_SCMode==6 || _SCMode==9) area=4.0*halfSize.x*halfSize.z;
    float coverage=clamp(_SCFaceCoverage,0.05,0.7);
    shape.halfSize=halfSize;
    bool uprightHeaded=_SCUpright>0.5 && (_SCMode==2 || _SCMode==5);
    // In headed modes the 2.5-head reference height includes the intact head.
    // The body occupies the lower 60%, not another full-height cage behind it.
    shape.bodyHalfSize=halfSize*(uprightHeaded?0.6:1.0);
    float2 faceSize=_SCFaceSize.x>0.01 && _SCFaceSize.y>0.01?_SCFaceSize.xy:float2(headWidth,headHeight);
    shape.headScale=sqrt(coverage*area/max(faceSize.x*faceSize.y*0.78539816,0.0001));
    // Area-based face enlargement must leave the collar above the floor of the cylinder.
    if(_SCMode==7)shape.headScale=min(shape.headScale,halfSize.y*1.9/max(_SCHead.y-SCHeadCut()+headHeight*0.65,0.001));
    if(_SCMode==6 || _SCMode==8)
        shape.headScale=min(shape.headScale,min(halfSize.x*1.85/max(faceSize.x,headWidth),(_SCMode==6?halfSize.z:halfSize.y)*1.85/headHeight));
    if(_SCMode<=5)shape.headScale=halfSize.y*2.0/(headHeight*2.5);
    // Inflation grows the torso around the original head, rather than enlarging the face with it.
    if(_SCMode==3)shape.headScale=max(_SCSize,0.05);
    shape.bodyTop=halfSize.y*2.0;
    shape.panelScale=1.0;
    if(_SCUpright>0.5 && _SCMode<=5)
    {
        shape.bodyTop=max(halfSize.y*2.0-headHeight*shape.headScale*0.82,halfSize.y*0.5);
        if(uprightHeaded)shape.bodyTop=shape.bodyHalfSize.y*2.0;
        float span=max(_SCMeasure.x-min(_SCPanelBottom,_SCMeasure.x-0.1),0.1);
        shape.panelScale=clamp(shape.bodyTop/span,0.5,1.0);
    }
    return shape;
}

float SCHeadRelief(){return clamp(_SCLayer*2+0.13,0.13,0.35);}
float SCCapRelief(SCShape shape)
{
    return _SCMode==8?min(SCHeadRelief(),shape.halfSize.z/max(max(_SCHeadFront,0.03)*shape.headScale*2,0.001)):SCHeadRelief();
}

float3 SCHeadTarget(float3 p,SCShape shape)
{
    float3 halfSize=shape.halfSize;
    float scale=shape.headScale,headHeight=SCHeadHeight();
    float3 head=(p-float3(_SCHead.x,_SCHead.y,_SCHead.w))*scale;
    if(SCCapProjection())
    {
        float relief=SCCapRelief(shape),front=max(_SCHeadFront,0.03)*scale*relief;
        head.z*=relief;
        if(_SCMode==6)return SCRotateFace(head)+float3(0,halfSize.y*2+front*0.25,0);
        return head+float3(0,halfSize.y,halfSize.z-front*0.5);
    }
    if(_SCMode==3)return head+float3(0,halfSize.y*2-headHeight*scale*0.6,halfSize.z-max(_SCHeadFront,0.03)*scale);
    if(_SCMode==2 || _SCMode==5)
    {
        if(_SCUpright<0.5)return head+float3(0,halfSize.y*2+headHeight*scale*0.5,0);
        float front=shape.bodyHalfSize.z;
        return head+float3(0,halfSize.y*2-headHeight*scale*0.5,max(front-max(_SCHeadFront,0.03)*scale,0));
    }
    float3 anchor=float3(0,max(halfSize.y*2-headHeight*scale*0.65,halfSize.y),max(halfSize.z-headHeight*scale*0.2,0));
    // Align the actual front of the enlarged head with the chest plane. The old
    // fixed depth offset puts the head well ahead of the chest (a hunched profile).
    if(_SCMode<=5) anchor.z=lerp(anchor.z,halfSize.z-max(_SCHeadFront,0.03)*scale,saturate(_SCUpright));
    if(SCBoxFace())anchor.y=halfSize.y*2-headHeight*scale*0.85;
    if(_SCMode==6 || _SCMode==9)
        return SCRotateFace(head)+float3(0,halfSize.y*2-headHeight*scale*0.2,0);
    return head+anchor;
}

// A star-shaped cage around the torso. The radial function is strictly
// increasing: d[r/(k+(1-k)r)]/dr = k/(k+(1-k)r)^2 > 0.
// Compress relief without reversing the order of nested skin/clothing layers.
// No height clamp, cap fold, or per-mesh smoothing that splits shared seams.
float3 SCEnvelope(float3 p,SCShape shape,float amount)
{
    bool withHead=_SCMode==2 || _SCMode==5;
    float top=withHead?_SCMeasure.x:1.0;
    float bottom=min(_SCPanelBottom,top-0.2);
    float3 sourceHalf=float3(max(_SCMeasure.z,0.025),(top-bottom)*0.5,max(_SCMeasure.w,0.025));
    float3 sourceCenter=float3(0,(top+bottom)*0.5,0);
    if(_SCProxy>0.5){sourceHalf.y=0.5;sourceCenter.y=0.5;}
    float3 q=(p-sourceCenter)/sourceHalf;
    float r=length(q);
    float3 originalDirection=SCNormalize(q,float3(0,1,0));
    float3 n=SCRotateFace(originalDirection);
    // A narrow rounded edge keeps the cage differentiable at box corners and
    // cylinder rims; a hard max creates a crease inside individual triangles.
    float maximum=max(max(abs(n.x),abs(n.y)),abs(n.z));
    float exponent=32.0;
    float3 power=pow(abs(n)/max(maximum,0.001),exponent);
    float denominator=maximum*pow(power.x+power.y+power.z,1.0/exponent);
    if(_SCMode==3 || _SCMode==4 || _SCMode==5)denominator=1;
    if(_SCMode==6 || _SCMode==7)denominator=pow(pow(length(n.xz),32.0)+pow(abs(n.y),32.0),0.03125);
    if(_SCMode==8)denominator=pow(pow(length(n.xy),32.0)+pow(abs(n.z),32.0),0.03125);
    float k=clamp(_SCLayer,0.002,0.15);
    // Log interpolation keeps radial order without overshooting long limbs on the way into the cage.
    float s,c;sincos((_SCMode==6 || _SCMode==9)?amount*1.570796327:0,s,c);
    float3 direction=float3(originalDirection.x,c*originalDirection.y+s*originalDirection.z,-s*originalDirection.y+c*originalDirection.z);
    float radialScale=_SCProxy>0.5?1/max(r,0.0001):1/(k+(1-k)*r);
    return direction*r*pow(radialScale/max(denominator,0.001),amount)
        *sourceHalf*pow(max(shape.bodyHalfSize/sourceHalf,0.0001),amount)
        +lerp(sourceCenter,float3(0,shape.bodyHalfSize.y,0),amount);
}

bool SCReferenceFlow()
{
    return _SCProxy<0.5 && _SCSmoothEnvelope>0.5 && _SCPreserveSurface>0.5 && (_SCMode<=5 || _SCMode==7);
}

bool SCHeadProjection()
{
    return SCReferenceFlow() && _SCUpright>0.5 && _SCAppendage<0.5 && (_SCMode==0 || _SCMode==1 || _SCMode==3 || _SCMode==4 || _SCMode==7);
}

float SCHeadSurfaceDepth(float2 p,SCShape shape)
{
    if(SCBoxFace())return shape.halfSize.z+shape.halfSize.y*0.035;
    if(_SCMode==0 || _SCMode==1)return shape.halfSize.z;
    float2 q=p/shape.halfSize.xy-float2(0,1);
    if(_SCMode==7)q.y=0;
    // shortcut: the outer cap keeps finite slopes; fit extreme hair overhangs separately if needed.
    return shape.halfSize.z*sqrt(max(1-dot(q,q),0.04));
}

float SCFrontSocketZ(float3 target,SCShape shape,int mode,float3 center,float headSize)
{
    float2 radius=float2(max(_SCHead.z,0.03)*1.25,headSize*0.8)*shape.headScale;
    float2 socketCenter=center.xy-float2(0,headSize*shape.headScale*0.15);
    float edge=length((target.xy-socketCenter)/radius);
    float blend=(1.0-smoothstep(1.0,1.6,edge))*smoothstep(0.0,shape.halfSize.z*0.2,target.z);
    float depth=center.z-max(_SCHeadFront,0.03)*shape.headScale*0.15;
    if(SCBoxFace())depth=shape.halfSize.z-max(_SCHeadFront,0.03)*shape.headScale*SCHeadRelief();
    if(mode==3 && _SCSmoothEnvelope>0.5)depth=min(depth,SCHeadSurfaceDepth(center.xy,shape)-max(_SCHeadFront,0.03)*shape.headScale*0.65);
    return lerp(target.z,min(target.z,depth),blend);
}

// Ordered horizontal sections avoid the radial cage pulling a collar sideways
// across the face. Height is strictly increasing, and each nested clothing ring
// keeps its radial order. The entire cross-section shares the same map.
float3 SCReferencePosition(float3 p,float3 masks,SCShape shape,float amount)
{
    float cut=SCHeadCut();
    float3 headCenter=SCHeadTarget(float3(_SCHead.x,_SCHead.y,_SCHead.w),shape);
    float cutY=headCenter.y+(cut-_SCHead.y)*shape.headScale;
    // Ground and collar are exact anchors; the positive rational map also matches the head slope.
    float k=max(shape.headScale*cut/max(cutY,0.05),0.001);
    float t=p.y/cut;
    float y=p.y>=cut?cutY+(p.y-cut)*shape.headScale:
        p.y<0?p.y*cutY/(cut*k):cutY*t/(k+(1-k)*t);
    float blend=SCHeadWeight(p,masks);
    float2 sourceHalf=max(_SCMeasure.zw,float2(0.025,0.025));
    float2 q=(p.xz-float2(_SCHead.x,_SCHead.w))/sourceHalf;
    float r=length(q);float2 direction=r>0.00001?q/r:float2(0,0);
    float radius=1;
    if(_SCMode==3 || _SCMode==4 || _SCMode==5)
    {
        float normalizedY=y/max(shape.bodyHalfSize.y,0.025)-1;
        radius=sqrt(max(1-normalizedY*normalizedY,0.0025));
    }
    float denominator=1;
    if(_SCMode<=2)denominator=pow(pow(abs(direction.x),32)+pow(abs(direction.y),32),1.0/32.0);
    // Retain enough depth to separate independently tessellated clothing layers.
    float layer=clamp(_SCLayer*4,0.08,0.4);
    float2 body=direction*r*pow(1/((layer+(1-layer)*r)*max(denominator,0.001)),amount)
        *sourceHalf*pow(max(shape.bodyHalfSize.xz*radius/sourceHalf,0.0001),amount)
        +float2(_SCHead.x,_SCHead.w)*(1-amount);
    float2 head=lerp(p.xz,(p.xz-float2(_SCHead.x,_SCHead.w))*shape.headScale+headCenter.xz,amount);
    float2 section=lerp(body,head,blend);
    float3 result=float3(section.x,lerp(p.y,y,amount),section.y);
    if(SCHeadProjection())
    {
        // A depth compression followed by a shear preserves XY and depth order.
        float depth=SCHeadRelief();
        result.z=lerp(result.z,SCHeadSurfaceDepth(result.xy,shape)+(result.z-shape.halfSize.z)*depth,blend*amount);
    }
    if(SCVolumeSurface() && _SCUseProxy>0.5)
    {
        float start=SCProxyStart();
        float tuck=0.7*smoothstep(start,start+(_SCMode==0?0.35:0.25),amount);
        float3 untucked=result;
        result.xz*=1-tuck*(1-blend);
        result.y+=tuck*max(0,lerp(cut,cutY,amount)-result.y);
        if(_SCMode==3 && _SCAccessory>0.5 && amount>start)
        {
            // The dressed body recedes under the opaque shell; its ornaments follow the exposed cross-section.
            float growthEnd=SCVolumeProxyGrowthEnd();
            float growth=smoothstep(start,growthEnd,amount);
            float handoff=smoothstep(growthEnd,1,amount);
            float centerY=lerp(0.5,shape.bodyHalfSize.y,amount);
            float3 sourceAxes=float3(sourceHalf.x,0.5,sourceHalf.y);
            float3 axes=max(sourceAxes*pow(max(shape.bodyHalfSize/sourceAxes,0.0001),amount)*growth,0.0001);
            float height=lerp(result.y,untucked.y,growth);
            height=lerp(height,clamp(height,centerY-0.999*axes.y,centerY+0.999*axes.y),handoff);
            float localY=(height-centerY)/axes.y;
            float shellRadius=sqrt(saturate(1-localY*localY));
            float2 axial=untucked.xz/axes.xz;
            float2 ringDirection=SCNormalize(float3(axial.x,0,axial.y),float3(0,0,-1)).xz;
            float liveRadius=length(result.xz/axes.xz)*(1-handoff);
            float2 ring=ringDirection*axes.xz*max(shellRadius,liveRadius);
            if(ringDirection.y>0 && shellRadius>0)
            {
                // The proxy's recessed face is evaluated at the endpoint and eased with its growth.
                float3 endpoint=float3(ringDirection.x*shellRadius,1+localY,ringDirection.y*shellRadius)*shape.bodyHalfSize;
                float shellContact=smoothstep(0,1,shellRadius/max(liveRadius,0.0001));
                ring.y+=(SCFrontSocketZ(endpoint,shape,3,headCenter,SCHeadHeight())-endpoint.z)*amount*growth*shellContact;
            }
            result=lerp(result,float3(ring.x,height,ring.y),1-blend);
        }
    }
    return result;
}

// One continuous map is shared by skin, clothes, outlines and shadows.
// No per-renderer bounds, vertex-index lookup, or time-varying world origin.
float3 SCTarget(float3 p, float3 masks, SCShape shape, float3 envelope)
{
    int mode = (int)round(clamp(_SCMode, 0.0, 12.0));
    bool withHead = mode == 2 || mode == 5;
    float neck = clamp(_SCMeasure.x, 0.25, 0.95);
    float headSize = SCHeadHeight();
    float sourceTop = withHead ? neck : 1.0;
    float3 sourceCenter = float3(0, sourceTop * 0.5, 0);
    float3 sourceHalf = float3(max(_SCMeasure.z,0.025), sourceTop * 0.5, max(_SCMeasure.w,0.025));
    float3 q = (p - sourceCenter) / sourceHalf;
    float bodyBase = min(_SCBodyBase,neck-0.1)*(_SCUpright>0.5?1:1-SCLegKeep());
    float faceStart = clamp(_SCFaceStart,0.3,0.85);
    float bodyY = (p.y-bodyBase)/max(neck-bodyBase,0.1);
    float mappedHeight = withHead ? bodyY : p.y<neck ? bodyY*faceStart : faceStart+(p.y-neck)/max(1.0-neck,0.05)*(1.0-faceStart);
    q.y = (_SCProxy > 0.5 ? p.y : mappedHeight)*2.0-1.0;
    float3 halfSize = shape.bodyHalfSize;

    // A quarter turn sends the front-facing (+Z) face toward +Y.
    if (mode == 6 || mode == 9) q = float3(q.x,q.z,-q.y);
    // Height-preserving cylindrical coordinates prevent long hair/skirts from
    // migrating across the face, which radial projection from the hips causes.
    float y = clamp(q.y,-1.0,1.0);
    bool rounded = mode == 3 || mode == 4 || mode == 5 || mode == 6 || mode == 7;
    float radius = rounded ? length(q.xz) : max(abs(q.x),abs(q.z));
    float2 ring = q.xz / max(radius,0.04);
    float shell = 1.0 + clamp(_SCLayer,0.002,0.15)*clamp(log2(max(radius,0.125)),-2.0,2.0);
    if (_SCProxy > 0.5) shell=1.0;
    float ringScale = 1.0;
    float mappedY = y;
    if (mode == 3 || mode == 4 || mode == 5)
        ringScale = sqrt(saturate(1.0-y*y));
    else
    {
        // Fold the last 6% onto the end caps instead of leaving open ends.
        mappedY = clamp(y/0.94,-1.0,1.0);
        ringScale = sqrt(saturate((1.0-abs(y))/0.06));
    }
    float3 target = float3(ring.x*ringScale*shell,mappedY,ring.y*ringScale*shell)*halfSize;
    if (mode == 8)
    {
        // A depth-compressed sphere gives a closed coin, including both faces.
        float3 sphere = SCNormalize(q,float3(0,1,0));
        target = float3(sphere.xy,clamp(q.z*8.0,-1.0,1.0))*halfSize;
    }
    if(_SCSmoothEnvelope>0.5)target=envelope-float3(0,halfSize.y,0);
    float hiddenLimb = max(_SCArms > 1.5 ? masks.y : 0, _SCLegs > 1.5 ? masks.z : 0);
    target *= lerp(1.0,0.65,smoothstep(0.1,0.9,hiddenLimb));
    target.y += halfSize.y;

    if(_SCProxy>0.5 && _SCUpright>0.5 && _SCPreserveSurface>0.5 && (mode<=5 || mode==7 && _SCSmoothEnvelope>0.5 || SCCapProjection()))
    {
        // Recess a continuous socket behind the real head. Keep the shell closed
        // instead of clipping a hard oval hole around the face.
        float3 center=SCHeadTarget(float3(_SCHead.x,_SCHead.y,_SCHead.w),shape);
        if(SCCapProjection())
        {
            // A socket fading halfway down the face intersects the retained chin.
            float2 radius=float2(max(_SCHead.z,0.03)*1.25,headSize*0.8)*shape.headScale;
            float2 offset=mode==6?(target.xz-center.xz-float2(0,headSize*shape.headScale*0.15))/radius:
                (target.xy-center.xy+float2(0,headSize*shape.headScale*0.15))/radius;
            float blend=(1-smoothstep(1,1.6,length(offset)))*(mode==6?smoothstep(halfSize.y,halfSize.y*1.5,target.y):smoothstep(0,halfSize.z*0.2,target.z));
            float depth=(mode==6?center.y:center.z)-max(_SCHeadFront,0.03)*shape.headScale*SCCapRelief(shape)*(mode==6?1.15:0.15);
            if(mode==6)target.y=lerp(target.y,min(target.y,depth),blend);
            else target.z=lerp(target.z,min(target.z,depth),blend);
        }
        else target.z=SCFrontSocketZ(target,shape,mode,center,headSize);
    }

    if (_SCProxy < 0.5 && _SCPreserveSurface > 0 && _SCAppendage<0.5 && _SCSmoothEnvelope<0.5)
    {
        // Use a uniform scale for upright torso panels; retain their relief.
        // Thin shapes retain the original relief instead of crossing front/back.
        float panelBottom=(_SCUpright>0.5 && mode<=5)?min(_SCPanelBottom,neck-0.1):bodyBase;
        float3 rest = p-float3(0,(panelBottom+neck)*0.5,0);
        float panelScale=shape.panelScale;
        rest*=panelScale;
        float3 rigid = rest;
        float front = p.z>=0 ? 1.0 : -1.0;
        float faceDepth = (_SCMode==6 || _SCMode==9) ? halfSize.y : halfSize.z;
        float follow=saturate(_SCSurfaceFollow);
        rigid.z += front*max(faceDepth-max(_SCMeasure.w,0.025)*panelScale,0);
        float centerY=(_SCUpright>0.5 && mode<=5) ? shape.bodyTop-(neck-panelBottom)*panelScale*0.5 : halfSize.y;
        rigid = SCRotateFace(rigid)+float3(0,centerY,0);
        rest = SCRotateFace(rest)+float3(0,centerY,0);
        float3 surface = lerp(rest,target,follow);
        surface = lerp(surface,rigid,SCPanelWeight(p,masks));
        float limb=max((1-SCArmKeep())*masks.y,(1-SCLegKeep())*masks.z);
        target = lerp(target,surface,saturate(_SCPreserveSurface)*(1.0-smoothstep(0.05,0.8,limb)));
    }

    if (withHead && _SCPreserveSurface<0.5)
    {
        target = lerp(target,SCHeadTarget(p,shape),SCHeadWeight(p,masks));
    }
    if(_SCProxy<0.5 && _SCPreserveSurface>0.5 && _SCAppendage<0.5)
        target=lerp(target,SCHeadTarget(p,shape),SCHeadWeight(p,masks));
    // Optional kept limbs retain size and meet the deformed shoulder/hip area.
    float keep = SCLimbWeight(masks);
    float3 limbTarget=_SCRigReady>0.5 ? SCLimbTarget(p,masks,halfSize) : p+float3(0,halfSize.y-sourceCenter.y,0);
    if(_SCRigReady>0.5 && _SCUpright>0.5 && mode<=5 && mode!=3 && masks.y>=masks.z) limbTarget.y+=shape.bodyTop*0.9-halfSize.y*1.38;
    target = lerp(target,limbTarget,keep);
    target.y+=SCBodyLift(halfSize);
    return target;
}

float SCAttachmentScale(float3 p,float3 masks,SCShape shape)
{
    if(_SCMode>=9)return lerp(1.0,min(SCFlatScale().x,SCFlatScale().y),saturate(_SCAccessoryAutoScale))*max(_SCAccessoryScale,0.01);
    // Use the owning region's uniform scale, also for its skinned displacement.
    // Preserved head/torso/limbs have explicit scales; for fully projected bodies
    // use the square-root of the frontal area ratio, without axis-wise squash.
    float3 size=shape.bodyHalfSize;
    float bodyScale=_SCPreserveSurface>0.5 && _SCSmoothEnvelope<0.5?shape.panelScale:
        clamp(sqrt(size.x*size.y/max(_SCMeasure.z*(_SCMeasure.x-_SCPanelBottom)*0.5,0.001)),0.5,2.0);
    float headKeep=(_SCPreserveSurface>0.5 || _SCMode==2 || _SCMode==5)?SCHeadWeight(p,masks):0;
    float scale=lerp(bodyScale,shape.headScale,headKeep);
    float keep=SCLimbWeight(masks);
    float3 limbStretch=SCLimbStretch(masks,size);
    float limbScale=_SCMode==3?max(limbStretch.x,max(limbStretch.y,limbStretch.z)):SCLimbScale(masks,size);
    scale=clamp(lerp(scale,limbScale,keep),_SCMode==3?0.01:0.25,6.0);
    return lerp(1.0,scale,saturate(_SCAccessoryAutoScale))*max(_SCAccessoryScale,0.01);
}

float3 SCAccessoryRotation(float3 p,float angle)
{
    float s,c; sincos(angle,s,c);
    return float3(p.x,c*p.y+s*p.z,-s*p.y+c*p.z);
}

float3 SCAxisRotation(float3 p,float3 axis,float angle)
{
    float s,c;sincos(angle,s,c);
    return p*c+cross(axis,p)*s+axis*dot(axis,p)*(1.0-c);
}

// Positions and contact frames must use the same intermediate deformation.
// Blending endpoint normals rotates attachments away from the surface at 50%.
float3 SCPosition(float3 p,float3 masks,SCShape shape,float amount)
{
    if(SCOriginalFlat())return SCFlatPosition(p,amount);
    if(SCReferenceFlow())
    {
        float3 limb=SCLimbTarget(p,masks,shape.bodyHalfSize);
        if(_SCRigReady>0.5 && _SCUpright>0.5 && _SCMode<=5 && _SCMode!=3 && masks.y>=masks.z)limb.y+=shape.bodyTop*0.9-shape.bodyHalfSize.y*1.38;
        return lerp(SCReferencePosition(p,masks,shape,amount),lerp(p,limb,amount),SCLimbWeight(masks))
            +float3(0,SCBodyLift(shape.bodyHalfSize)*amount,0);
    }
    float3 envelope=_SCSmoothEnvelope>0.5?SCEnvelope(p,shape,1):0;
    float3 result=lerp(p,SCTarget(p,masks,shape,envelope),amount);
    float keep=SCLimbWeight(masks);
    float head=(_SCProxy<0.5 && _SCAppendage<0.5 && (_SCPreserveSurface>0.5 || _SCMode==2 || _SCMode==5))?SCHeadWeight(p,masks)*(1-keep):0;
    if(_SCSmoothEnvelope>0.5 && !(_SCProxy>0.5 && _SCMode==0 && SCVolumeSurface()))
    {
        result+=(SCEnvelope(p,shape,amount)-lerp(p,envelope,amount))*(1-head)*(1-keep);
        result+=SCRotateFace(float3(0,0,2*shape.halfSize.z*amount*(1-amount)))*head;
    }
    // Grow a closed surface from inside the torso before releasing its original outline.
    if(SCVolumeSurface())
    {
        float start=SCProxyStart();
        float3 center=float3(0,lerp(0.5,shape.bodyHalfSize.y,amount)+SCBodyLift(shape.bodyHalfSize)*amount,0);
        if(_SCProxy>0.5) return lerp(center,result,smoothstep(start,SCVolumeProxyGrowthEnd(),amount));
        if(SCCapProjection() && _SCUseProxy>0.5)
            result=lerp(result,center,0.65*smoothstep(start,start+0.25,amount)*(1-head)*(1-keep));
    }
    return result;
}

float3 SCSkinnedMotion(float3 posed,float3 p,float3 masks,SCShape shape,float amount,float3 mapped)
{
    float preserve=SCLimbWeight(masks);
    float head=_SCProxy<0.5 && (_SCPreserveSurface>0.5 || _SCMode==2 || _SCMode==5) && _SCAppendage<0.5 ? SCHeadWeight(p,masks)*(1-preserve) : 0;
    float panel=_SCProxy<0.5 && _SCAppendage<0.5 ? SCPanelWeight(p,masks)*(1-preserve)*(1-head) : 0;
    float3 motion=posed*(1-amount)+amount*(posed*preserve*SCLimbStretch(masks,shape.bodyHalfSize)
        +SCRotateFace(posed)*(panel*shape.panelScale+head*shape.headScale));
    if(SCCapProjection())motion+=amount*head*shape.headScale*SCRotateFace(float3(0,0,posed.z*(SCCapRelief(shape)-1)));
    if(SCHeadProjection())
    {
        mapped.y-=SCBodyLift(shape.bodyHalfSize)*amount;
        float weight=SCHeadWeight(p,masks)*amount*(1-preserve);
        motion.z*=lerp(1,SCHeadRelief(),weight);
        motion.z+=weight*(SCHeadSurfaceDepth(mapped.xy+motion.xy,shape)-SCHeadSurfaceDepth(mapped.xy,shape));
    }
    return motion;
}

float3 SCDeformedNormal(float3 normal,float3 targetNormal,float3 p,float3 masks,SCShape shape,float amount,float3 mapped)
{
    float preserve=SCLimbWeight(masks);
    float head=_SCProxy<0.5 && (_SCPreserveSurface>0.5 || _SCMode==2 || _SCMode==5) && _SCAppendage<0.5 ? SCHeadWeight(p,masks)*(1-preserve) : 0;
    float3 limbNormal=normal;
    if(_SCMode==3)limbNormal/=lerp(float3(1,1,1),SCLimbStretch(masks,shape.bodyHalfSize),amount);
    targetNormal=SCNormalize(lerp(targetNormal,limbNormal,preserve),normal);
    if(head>0.999)
    {
        targetNormal=normal;
        if(SCHeadProjection())
        {
            float2 q=mapped.xy/shape.halfSize.xy-float2(0,1);
            if(_SCMode==7)q.y=0;
            float curve=1-dot(q,q);
            float2 slope=(_SCMode==3 || _SCMode==4 || _SCMode==7) && curve>0.04?
                -shape.halfSize.z*q/(shape.halfSize.xy*sqrt(max(curve,0.04))):float2(0,0);
            float scale=lerp(1,SCHeadRelief(),amount);
            targetNormal=SCNormalize(float3(scale*normal.xy-amount*slope*normal.z,normal.z),normal);
        }
        if(_SCMode==6)
        {
            float c=1-amount,d=amount*shape.headScale,r=SCCapProjection()?SCCapRelief(shape):1;
            targetNormal=normalize(float3(normal.x/(c+d),(c*normal.y+d*normal.z)/(c*c+d*d*r),(-d*r*normal.y+c*normal.z)/(c*c+d*d*r)));
        }
        if(_SCMode==8 && SCCapProjection())targetNormal=normalize(float3(normal.xy*lerp(1,shape.headScale*SCCapRelief(shape),amount),normal.z*lerp(1,shape.headScale,amount)));
    }
    return SCNormalize(lerp(normal,targetNormal,saturate(_SCNormalBlend)),targetNormal);
}

void SCDeform(inout float4 vertex, inout float3 normal, inout float4 tangent,
    float2 uv4, float2 uv5, float2 uv6, float2 uv7,
    float3 restPosition,float3 restNormal,float3 restTangent,float restReady)
{
    float amount = SCActiveAmount();
    if (amount <= 0.0) return;
    float height = max(abs(_SCFrame.w),1e-4);
    float3 p = float3(uv4,uv5.x);
    float3 masks = saturate(float3(uv5.y,uv6));
    if(_SCMode>=13)
    {
        float3 posed=(vertex.xyz-_SCFrame.xyz)/height;
        float3 mapped=SCAddedBasePosition(posed,amount);
        float3 dx=(SCAddedBasePosition(posed+float3(0.0002,0,0),amount)-mapped)*5000;
        float3 dy=(SCAddedBasePosition(posed+float3(0,0.0002,0),amount)-mapped)*5000;
        float3 dz=(SCAddedBasePosition(posed+float3(0,0,0.0002),amount)-mapped)*5000;
        normal=SCNormalize(cross(dy,dz)*normal.x+cross(dz,dx)*normal.y+cross(dx,dy)*normal.z,normal);
        tangent.xyz=SCNormalize(dx*tangent.x+dy*tangent.y+dz*tangent.z,tangent.xyz);
        if(_SCMode==15)
        {
            float slope=max(_SCClothBend,0)*amount*(6.283185307/1.4)*cos(mapped.y/(1.4*max(_SCSize,0.05))*6.283185307);
            normal.y-=slope*(normal.z+0.35*normal.x);
            normal=normalize(normal);
            tangent.xz+=float2(0.35,1)*(slope*tangent.y);
            tangent.xyz=normalize(tangent.xyz);
            mapped.xz+=float2(0.35,1)*SCClothWave(mapped.y,amount);
        }
        vertex.xyz=mapped*height+_SCFrame.xyz;
        return;
    }
    if(SCOriginalFlat())
    {
        float3 scale=lerp(1.0,SCFlatScale(),amount);
        float3 posed=(vertex.xyz-_SCFrame.xyz)/height;
        scale.z=SCPlaneDepthScale(posed.z,max(_SCThickness,0.004)*max(_SCSize,0.05),amount);
        vertex.xyz=SCFlatPosition(posed,amount)*height+_SCFrame.xyz;
        normal=normalize(SCFlatRotate(normal/scale,amount));
        tangent.xyz=normalize(SCFlatRotate(tangent.xyz*scale,amount));
        return;
    }
    SCShape shape=SCGetShape();
    float3 n = SCDecodeNormal(uv7);
    float3 axis = abs(n.y) < 0.9 ? float3(0,1,0) : float3(1,0,0);
    float3 t = normalize(cross(axis,n));
    float3 b = cross(n,t);
    float3 mapped=SCPosition(p,masks,shape,amount);
    float3 dt = SCPosition(p + t*0.001,masks,shape,amount)-mapped;
    float3 db = SCPosition(p + b*0.001,masks,shape,amount)-mapped;
    // Derivatives use a 0.001 step. Remove that scale before normalization:
    // otherwise a thin/intermediate surface trips the zero-vector fallback.
    float3 targetNormal = SCNormalize(cross(dt*1000,db*1000), n);
    if(_SCAccessory>0.5)
    {
        // All vertices of an accessory share the same baked attachment anchor.
        // Apply a similarity transform to the already skinned mesh. The same
        // scale also carries bone motion into the enlarged attachment frame.
        float keep=SCLimbWeight(masks);
        float turn=(_SCMode==6 || _SCMode==9) ? 1.570796327*(1.0-keep) : 0;
        float angle=turn*amount;
        float3 attachment=mapped;
        float3 contactNormal=targetNormal;
        float3 pivot=p*height+_SCFrame.xyz;
        float scale=lerp(1.0,SCAttachmentScale(p,masks,shape),amount);
        float3 offset=vertex.xyz-pivot;
        float3 anchorMotion=0;
        float3 skinnedAnchorNormal=n;
        if(restReady>0.5)
        {
            float3 rn=normalize(restNormal),rt=SCNormalize(restTangent-rn*dot(rn,restTangent),t);
            float3 rb=cross(rn,rt),sn=normalize(normal),st=SCNormalize(tangent.xyz-sn*dot(sn,tangent.xyz),t);
            float3 sb=cross(sn,st);
            float3 restOffset=restPosition*height+_SCFrame.xyz-pivot;
            // Recover the skinned pivot separately; only the ornament's offset gets its similarity transform.
            offset=st*dot(restOffset,rt)+sb*dot(restOffset,rb)+sn*dot(restOffset,rn);
            anchorMotion=SCSkinnedMotion((vertex.xyz-offset-pivot)/height,p,masks,shape,amount,attachment)*height;
            skinnedAnchorNormal=st*dot(n,rt)+sb*dot(n,rb)+sn*dot(n,rn);
            contactNormal=SCDeformedNormal(skinnedAnchorNormal,contactNormal,p,masks,shape,amount,attachment+anchorMotion/height-float3(0,SCBodyLift(shape.bodyHalfSize)*amount,0));
        }
        float3 from=SCAccessoryRotation(skinnedAnchorNormal,angle);
        float keepPitch=0;
        if(_SCMode==3 && SCVolumeSurface() && _SCProxy<0.5 && _SCUseProxy>0.5 && amount>SCProxyStart())
        {
            // Follow the inflated shell's bearing without swinging a long ornament down with its slope.
            keepPitch=smoothstep(SCProxyStart(),SCVolumeProxyGrowthEnd(),amount)*(1-SCHeadWeight(p,masks))*(1-keep);
            float yaw=atan2(cross(from,contactNormal).y,dot(from.xz,contactNormal.xz)+1e-8)*keepPitch;
            float3 up=float3(0,1,0);
            from=SCAxisRotation(from,up,yaw);
            offset=SCAxisRotation(offset,up,yaw);
            normal=SCAxisRotation(normal,up,yaw);
            tangent.xyz=SCAxisRotation(tangent.xyz,up,yaw);
        }
        float3 contactAxis=SCNormalize(cross(from,contactNormal),SCAccessoryRotation(t,angle));
        float contactAngle=acos(clamp(dot(from,contactNormal),-1.0,1.0))*(1-keepPitch);
        offset=SCAxisRotation(SCAccessoryRotation(offset,angle),contactAxis,contactAngle)*scale;
        normal=SCAxisRotation(SCAccessoryRotation(normal,angle),contactAxis,contactAngle);
        tangent.xyz=SCAxisRotation(SCAccessoryRotation(tangent.xyz,angle),contactAxis,contactAngle);
        if(_SCMode==8 && SCCapProjection() && amount>0.5)
        {
            // Reserve the contact and outward relief. Compress a long inward offset before it reaches the other face.
            float side=SCHeadWeight(p,masks)>0.5 || p.z>=0?1:-1;
            float room=max(side*(attachment.z*height+anchorMotion.z),height*0.0001)*0.5;
            float inward=max(-side*offset.z-room,0);
            float depthRatio=room/(room+inward);
            float guard=smoothstep(0.5,0.75,amount);
            offset.z+=side*inward*(1-depthRatio)*guard;
            float depthScale=lerp(1,depthRatio*depthRatio,guard);
            normal=SCNormalize(float3(normal.xy*depthScale,normal.z),normal);
            tangent.xyz=SCNormalize(float3(tangent.xy,tangent.z*depthScale),tangent.xyz);
        }
        vertex.xyz=attachment*height+_SCFrame.xyz+anchorMotion+offset;
        return;
    }
    float3 posed=(vertex.xyz-_SCFrame.xyz)/height-p;
    float3 motion=SCSkinnedMotion(posed,p,masks,shape,amount,mapped);
    normal=SCDeformedNormal(normal,targetNormal,p,masks,shape,amount,mapped+motion-float3(0,SCBodyLift(shape.bodyHalfSize)*amount,0));
    // Transport the UV tangent itself; the arbitrary normal-derivative basis rotates normal maps.
    float3 targetTangent=dt*dot(tangent.xyz,t)+db*dot(tangent.xyz,b);
    float kept=SCLimbWeight(masks);
    float head=_SCProxy<0.5 && _SCAppendage<0.5 && (_SCPreserveSurface>0.5 || _SCMode==2 || _SCMode==5)?SCHeadWeight(p,masks)*(1-kept):0;
    // The input tangent is already skinned; transport protected regions with the same local map as bone motion.
    float protectedWeight=max(kept,head);
    if(protectedWeight>0)targetTangent=lerp(targetTangent,SCSkinnedMotion(tangent.xyz*0.001,p,masks,shape,amount,mapped+motion),protectedWeight);
    tangent.xyz=SCNormalize(targetTangent*1000-normal*dot(normal,targetTangent*1000),tangent.xyz);
    vertex.xyz=(mapped+motion)*height+_SCFrame.xyz;
}

void SCDeformPrepared(inout float4 vertex,inout float3 normal,inout float4 tangent,
    float2 uv4,float2 uv5,float2 uv6,float2 uv7,uint vertexID)
{
    float3 position=0,restNormal=0,restTangent=0;
    float ready=_SCAccessory>0.5 && _SCMode<9 && SCActiveAmount()>0?_SCAttachmentDataReady:0;
    if(ready>0.5)
    {
        float x=fmod(vertexID,_SCVertexData_TexelSize.z),row=floor(vertexID/_SCVertexData_TexelSize.z)*3;
        float2 uv=(float2(x,row)+0.5)*_SCVertexData_TexelSize.xy;
        position=tex2Dlod(_SCVertexData,float4(uv,0,0)).xyz;
        restNormal=tex2Dlod(_SCVertexData,float4(uv+float2(0,_SCVertexData_TexelSize.y),0,0)).xyz;
        restTangent=tex2Dlod(_SCVertexData,float4(uv+float2(0,2*_SCVertexData_TexelSize.y),0,0)).xyz;
    }
    SCDeform(vertex,normal,tangent,uv4,uv5,uv6,uv7,position,restNormal,restTangent,ready);
}

float SCHide(float2 weights)
{
    if(_SCMode>=13)return 0;
    float w = max(_SCArms > 1.5 ? weights.x : 0, _SCLegs > 1.5 ? weights.y : 0);
    return SCActiveAmount()*smoothstep(0.55,0.95,w);
}

float SCProxyBlend()
{
    return smoothstep(SCProxyStart(),1,saturate(_SCAmount))*_SCUseProxy*_SCEnabled*_SCReady;
}

float SCProxyFade(float3 p,float3 masks)
{
    if(SCPanel())return _SCProxy>0.5?0:SCProxyBlend();
    if(_SCMode>=9)return 0;
    if (_SCProxy > 0.5) return 0;
    // A wrist/ankle attachment cannot keep tracking the posed hand/foot after
    // that limb has completely disappeared into the body cage.
    if (_SCAccessory>0.5)
    {
        if(_SCMode==3)return SCProxyBlend()*max(masks.y*(_SCArms>1.5),masks.z*(_SCLegs>1.5));
        float stored=max(masks.y*(_SCArms>=0.5?1:smoothstep(0.7,1,_SCArmMorph)),masks.z*(_SCLegs>=0.5?1:smoothstep(0.7,1,_SCLegMorph)));
        return SCProxyBlend()*stored;
    }
    float head = (_SCMode > 1.5 && _SCMode < 2.5 || _SCMode > 4.5 && _SCMode < 5.5) ? smoothstep(0.05,0.95,masks.x) : 0;
    float kept=max(_SCArms<0.5?masks.y*(1-smoothstep(0.85,1.0,_SCArmMorph)):0,_SCLegs<0.5?masks.z*(1-smoothstep(0.85,1.0,_SCLegMorph)):0);
    if(_SCMode==3 && _SCAppendage<0.5)kept=SCLimbWeight(masks);
    if(_SCPreserveSurface>0.5) head=max(SCHeadWeight(p,masks),SCPanelWeight(p,masks));
    if(_SCAppendage>0.5) head=0;
    // Partially projected torso triangles must give way around the solid shell's collar.
    head=SCVolumeSurface() && !SCCapProjection()?smoothstep(0.8,0.98,head):smoothstep(0.01,0.08,head);
    return SCProxyBlend()*(1.0-max(head,kept));
}

bool SCAxialCylinder(){return SCVolumeSurface() && (_SCMode==6 || _SCMode==7);}
bool SCHeadAtlas(){return _SCUpright>0.5 && _SCPreserveSurface>0.5 && _SCMode<=5 && _SCMode!=2 && _SCMode!=5;}

float4 SCSurfaceData(float3 p,float3 masks)
{
    float2 sourceUV=0;
    if(_SCProxy>0.5 && SCHeadAtlas())
        sourceUV=float2(atan2(p.x/max(_SCMeasure.z,0.025),p.z/max(_SCMeasure.w,0.025))*0.159154943+0.5,p.y);
    return float4(SCHide(masks.yz),SCProxyFade(p,masks),sourceUV);
}

float SCAtlasBodyBase(float neck)
{
    return min(_SCBodyBase,neck-0.1)*(_SCUpright>0.5?1:1-SCLegKeep());
}

float2 SCAtlasUV(float2 uv,float3 p)
{
    if(SCCubeAppearance() || SCAxialCylinder())return uv;
    if(SCPanel())
    {
        float3 mapped=SCAddedBasePosition(p,1);
        float size=max(_SCSize,0.05),width=max(_SCPanelWidth,0.1)*size;
        float3 layout=SCPanelLayout();float y=SCPanelSourceHeight(mapped.y,layout);
        float2 section=SCPanelSection(y);
        float2 faceUV=float2((mapped.x/width*section.y+section.x)/1.1+0.5,(y+0.05)/1.1);
        // The rear capture uses the opposite horizontal camera direction.
        faceUV.x=p.z>=0?1-faceUV.x:faceUV.x;
        return float2(saturate(faceUV.x)*0.5+(p.z>=0?0:0.5),saturate(faceUV.y));
    }
    float y=uv.y;
    float neck=clamp(_SCMeasure.x,0.25,0.95);
    float bodyBase=SCAtlasBodyBase(neck);
    if(SCHeadAtlas())
    {
        SCShape shape=SCGetShape();
        float split=clamp(shape.bodyTop/(shape.halfSize.y*2.0),0.1,0.9);
        // The real face occupies the opening. Fill the surrounding upper shell
        // from the back of the head, so the atlas does not draw a second face.
        if(y>=split)uv.x=frac(uv.x*0.5+0.75);
        uv.y=y<split ? lerp(_SCPanelBottom,neck,y/split) : lerp(neck,1.0,(y-split)/(1.0-split));
        return uv;
    }
    if(_SCPreserveSurface>0.5 || _SCMode==2 || _SCMode==5) { uv.y=lerp(bodyBase,neck,y);return uv; }
    float faceStart=clamp(_SCFaceStart,0.3,0.85);
    uv.y=y<faceStart ? lerp(bodyBase,neck,y/faceStart) : lerp(neck,1.0,(y-faceStart)/(1.0-faceStart));
    return uv;
}

float3 SCAtlasDirection(float2 uv)
{
    float sine,cosine;sincos((uv.x-0.5)*6.283185307,sine,cosine);
    float y=uv.y*2-1,radial=sqrt(saturate(1-y*y));
    return float3(sine*radial,y,cosine*radial);
}

float2 SCCylinderSideUV(float2 uv)
{
    float3 direction=SCRotateFace(SCAtlasDirection(uv));
    float radius=length(direction.xz);
    float angle=radius>0.0001?atan2(direction.x,direction.z):0;
    float height=0.5+0.5*direction.y/max(radius,abs(direction.y));
    float neck=clamp(_SCMeasure.x,0.25,0.95);
    return float2(angle*0.159154943+0.5,lerp(SCAtlasBodyBase(neck),neck,height));
}

float4 SCCubeTile(float tile,float2 uv)
{
    float2 grid=float2(4,3);
    float2 inset=0.5*_SCCubeTex_TexelSize.xy*grid;
    return tex2D(_SCCubeTex,(float2(fmod(tile,grid.x),floor(tile/grid.x))+lerp(inset,1-inset,saturate(uv)))/grid);
}

float SCCubeStored(float control,float morph)
{
    return control<0.5?smoothstep(0.8,1,morph):1-step(1.5,control);
}

float4 SCBottomPaint(float2 uv)
{
    return lerp(SCCubeTile(5,uv),SCCubeTile(9,uv),_SCMode==3?0:SCCubeStored(_SCLegs,_SCLegMorph));
}

float4 SCCylinderCapColor(float2 uv,float4 legacy)
{
    if(_SCCubeReady<0.5)return legacy;
    float3 p=SCRotateFace(SCAtlasDirection(uv));
    float radius=length(p.xz),height=abs(p.y);
    float cap=smoothstep(0.94,1.06,height/max(radius,0.001));
    float2 face=float2(p.y>=0?p.x:-p.x,p.z)/max(radius,height)*0.5+0.5;
    float4 paint=p.y>=0?SCCubeTile(4,face):SCBottomPaint(face);
    return lerp(legacy,paint,cap*paint.a);
}

float4 SCCubeSide(float tile,float storedTile,float2 uv,float split,float4 legacy)
{
    float cloth=1-smoothstep(split-0.05,split+0.045,uv.y);
    uv.y/=split;
    float4 color=SCCubeTile(tile,uv);
    if(storedTile>=0)color=lerp(color,SCCubeTile(storedTile,uv),SCCubeStored(_SCArms,_SCArmMorph));
    return lerp(legacy,color,cloth*color.a);
}

float4 SCCubeColor(float2 uv,float split,float4 legacy)
{
    // Recover the cage direction from its continuous longitude and height, including the back seam.
    float3 p=SCAtlasDirection(uv);
    float3 axes=abs(p);
    if(_SCMode==8)
    {
        float radial=length(p.xy);
        float weight=smoothstep(0.94,1.06,axes.z/max(radial,0.001));
        float2 face=float2(p.z>=0?-p.x:p.x,p.y)/max(radial,axes.z)*0.5+0.5;
        return lerp(legacy,SCCubeSide(p.z>=0?0:2,p.z>=0?6:-1,face,split,legacy),weight);
    }
    float largest=max(axes.x,max(axes.y,axes.z));
    float3 weights=smoothstep(largest*0.90,largest,axes);
    p/=max(largest,0.001);
    float4 color=0;
    if(weights.z>0)
        color+=weights.z*SCCubeSide(p.z>=0?0:2,p.z>=0 && _SCMode!=3?6:-1,float2(p.z>=0?-p.x:p.x,p.y)*0.5+0.5,split,legacy);
    if(weights.x>0)
        color+=weights.x*SCCubeSide(p.x>=0?1:3,_SCMode==3?-1:p.x>=0?7:8,float2(p.x>=0?p.z:-p.z,p.y)*0.5+0.5,split,legacy);
    if(weights.y>0)
    {
        float2 cap=float2(p.y>=0?p.x:-p.x,p.z)*0.5+0.5;
        float4 paint=legacy;
        if(p.y<0)
        {
            if(_SCMode==1)cap.y+=0.23;
            paint=SCBottomPaint(cap);
        }
        else if(_SCMode==1)paint=SCCubeSide(0,6,float2(1-cap.x,cap.y),1,legacy);
        else if(_SCMode==0 || _SCMode==4)paint=SCCubeTile(4,cap);
        color+=weights.y*lerp(legacy,paint,paint.a);
    }
    return color/(weights.x+weights.y+weights.z);
}

void SCClip(float2 data, float2 screen)
{
    clip(0.98-data.x);
    float noise=frac(52.9829189*frac(dot(floor(screen),float2(0.06711056,0.00583715))));
    bool solid=SCVolumeSurface() || SCPanel();
    // Retain coverage through the narrow hand-over band at the endpoint too;
    // thresholding it at 0.5 produces a visible cut around protected patches.
    if(_SCProxy>0.5)
    {
        if(!SCPanel())clip(8.5-_SCMode);
        clip(SCProxyBlend()-(solid?0:noise)-0.00001);
    }
    else if(solid)
        clip(0.5-data.y);
    else
        clip(noise-data.y+0.000001);
}

// Blend sampled colours at the hair/torso boundary, not discontinuous UVs.
#define SC_APPLY_PROXY_COLOR(color,uv,sourceUV,SAMPLE,SAMPLE_GRAD) \
    if(_SCProxy>0.5 && SCPanel())color=tex2D(_SCSheetTex,uv); \
    if(_SCProxy>0.5 && SCAxialCylinder()) \
    { \
        float2 sideUV=SCCylinderSideUV(uv); \
        float2 sideDx=ddx(sideUV),sideDy=ddy(sideUV); \
        /* The repeat seam must not select a coarse mip. */ \
        sideDx.x-=round(sideDx.x);sideDy.x-=round(sideDy.x); \
        color=SCCylinderCapColor(uv,SAMPLE_GRAD(sideUV,sideDx,sideDy)); \
    } \
    if(_SCProxy>0.5 && SCHeadAtlas()) \
    { \
        SCShape scShape=SCGetShape(); \
        float scSplit=clamp(scShape.bodyTop/(scShape.halfSize.y*2.0),0.1,0.9); \
        float2 scUV=sourceUV; \
        float2 scBodyUV=float2(scUV.x,lerp(_SCPanelBottom,_SCMeasure.x,saturate(scUV.y/scSplit))); \
        float2 scHeadUV=float2(frac(scUV.x*0.5+0.75),lerp(_SCMeasure.x,1.0,saturate((scUV.y-scSplit)/(1-scSplit)))); \
        color=lerp(SAMPLE(scBodyUV),SAMPLE(scHeadUV),smoothstep(scSplit-0.08,scSplit+0.08,scUV.y)); \
        if(SCCubeAppearance())color=SCCubeColor(uv,scSplit,color); \
    } \
    else if(SCCubeAppearance()) \
    { \
        float neck=clamp(_SCMeasure.x,0.25,0.95); \
        float2 bodyUV=float2((uv).x,lerp(SCAtlasBodyBase(neck),neck,(uv).y)); \
        color=SCCubeColor(uv,_SCMode==8?0.6:1,SAMPLE(bodyUV)); \
    }

#define LIL_CUSTOM_VERTEX_OS SCDeformPrepared(positionOS,input.normalOS,input.tangentOS,input.uv4,input.uv5,input.uv6,input.uv7,input.vertexID); if(_SCProxy>0.5) { input.uv0.xy=SCAtlasUV(input.uv0.xy,float3(input.uv4,input.uv5.x)); uvMain.xy=input.uv0.xy; }
#define LIL_CUSTOM_V2F_MEMBER(id0,id1,id2,id3,id4,id5,id6,id7) float4 scData : TEXCOORD##id0;
#define LIL_CUSTOM_VERT_COPY LIL_V2F_OUT_BASE.scData = SCSurfaceData(float3(input.uv4,input.uv5.x),float3(input.uv5.y,input.uv6));
#define BEFORE_UNPACK_V2F SCClip(input.scData.xy,input.positionCS.xy);
#define SC_LIL_MAIN(uv) LIL_SAMPLE_2D(_MainTex,sampler_MainTex,uv)
#define SC_LIL_MAIN_GRAD(uv,dx,dy) LIL_SAMPLE_2D_GRAD(_MainTex,sampler_MainTex,uv,dx,dy)
#define OVERRIDE_MAIN \
    LIL_GET_MAIN_TEX \
    SC_APPLY_PROXY_COLOR(fd.col,fd.uvMain,input.scData.zw,SC_LIL_MAIN,SC_LIL_MAIN_GRAD) \
    LIL_APPLY_MAIN_TONECORRECTION \
    fd.col*=_Color;
#endif
