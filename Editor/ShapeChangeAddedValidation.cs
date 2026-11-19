using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Object=UnityEngine.Object;

namespace ShapeChange
{
    public static partial class ShapeChangeValidation
    {
        static void CheckAddedModes(GameObject root,StringBuilder report)
        {
            var strip=new Color[33];strip[0]=Color.red;strip[32]=Color.blue;
            var softened=ShapeChangeShell.ExtendCaptureColors(strip,strip.Length,1,softenGaps:true);
            if(softened[0]!=Color.red || softened[32]!=Color.blue || softened[16].r<=0 || softened[16].b<=0)
                throw new Exception("Panel gap blending must preserve photographed colors and soften the boundary in the gap.");
            var template=ShapeChangeSetup.Materials(root).First(m=>m.GetFloat("_SCProxy")<0.5f);
            var probe=new Material(Shader.Find("Hidden/ShapeChange/Validation"));
            probe.CopyPropertiesFromMaterial(template);CopyPosture(template,probe);
            var points=new System.Collections.Generic.List<Color>();
            foreach(float x in new[]{-0.35f,-0.08f,0.03f,0.3f})
            foreach(float y in new[]{-0.2f,0f,0.15f,0.45f,0.8f,1.05f})
            foreach(float z in new[]{-0.25f,-0.04f,0.02f,0.18f})
            foreach(var d in new[]{Vector3.zero,Vector3.right,Vector3.up,Vector3.forward})
            {var p=new Vector3(x,y,z)+d*0.001f;points.Add(new Color(p.x,p.y,p.z,0));}
            var input=new Texture2D(points.Count,1,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point};
            var output=new Texture2D(points.Count,1,TextureFormat.RGBAFloat,false,true);
            var rt=new RenderTexture(points.Count,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var previous=RenderTexture.active;int cells=0,contacts=0;float minimum=float.PositiveInfinity;
            Func<Color[]> sample=()=>{Graphics.Blit(input,rt,probe);RenderTexture.active=rt;output.ReadPixels(new Rect(0,0,points.Count,1),0,0);output.Apply();return output.GetPixels();};
            try
            {
                input.SetPixels(points.ToArray());input.Apply();
                probe.SetFloat("_SCEnabled",1);probe.SetFloat("_SCReady",1);probe.SetFloat("_SCProxy",0);
                probe.SetFloat("_SCSize",1);
                probe.SetFloat("_SCPressSpread",0.5f);probe.SetFloat("_SCClothBend",0.12f);
                foreach(int mode in new[]{13,14,15})foreach(float thickness in new[]{0.004f,0.08f})foreach(float width in new[]{0.35f,0.9f})
                for(int step=0;step<=8;step++)
                {
                    float amount=step/8f;
                    probe.SetFloat("_SCMode",mode);probe.SetFloat("_SCThickness",thickness);probe.SetFloat("_SCPanelWidth",width);probe.SetFloat("_SCAmount",amount);
                    probe.SetFloat("_ProbeReference",1);var values=sample();
                    // Undo the analytically unit-determinant shear before
                    // finite-difference volume measurement. Curved, ultra-thin
                    // cells otherwise measure truncation error, not dF/dp.
                    if(mode==15)for(int i=0;i<values.Length;i++)
                    {
                        float wave=0.12f*amount*Mathf.Sin(values[i].g/1.4f*2*Mathf.PI);
                        values[i].r-=0.35f*wave;values[i].b-=wave;
                    }
                    for(int i=0;i<values.Length;i+=4)
                    {
                        Vector3 p=RGB(values[i]);var dx=(RGB(values[i+1])-p)*1000;var dy=(RGB(values[i+2])-p)*1000;var dz=(RGB(values[i+3])-p)*1000;
                        float det=Vector3.Dot(dx,Vector3.Cross(dy,dz));minimum=Mathf.Min(minimum,det);
                        if(!Finite(det) || det<=0)throw new Exception("New mode field reverses order: "+mode+" amount="+amount+" thickness="+thickness+" input="+RGB(points[i]).ToString("F6")+" output="+p.ToString("F8")+" dx="+dx.ToString("F8")+" dy="+dy.ToString("F8")+" dz="+dz.ToString("F8")+" det="+det);
                        if(amount==0 && (p-RGB(points[i])).magnitude>0.000003f)throw new Exception("New mode zero is not identity.");
                        if(mode==13 && points[i].g==0 && Mathf.Abs(p.y)>0.000001f)throw new Exception("Press moves floor contact.");
                        if(mode==13 && amount>0 && points[i].g>0 && p.y>=points[i].g)throw new Exception("Press does not reduce height: input="+RGB(points[i]).ToString("F6")+" output="+p.ToString("F8")+" amount="+amount+" thickness="+thickness);
                        if(mode==13 && amount==1 && points[i]==new Color(.03f,.45f,.02f,0) && p.x<.045f)
                            throw new Exception("Press did not spread the central footprint toward the rounded endpoint.");
                        cells++;
                    }
                    probe.SetFloat("_ProbeReference",0);probe.SetFloat("_ProbeSkinned",1);
                    foreach(var pose in new[]{Matrix4x4.identity,Matrix4x4.TRS(new Vector3(0.01f,0.015f,-0.01f),Quaternion.Euler(8,12,-4),Vector3.one)})
                    {
                        probe.SetMatrix("_ProbePose",pose);probe.SetFloat("_SCAccessory",0);var body=sample();
                        probe.SetFloat("_SCAccessory",1);var accessory=sample();
                        for(int i=0;i<body.Length;i++)
                        {
                            if((RGB(body[i])-RGB(accessory[i])).magnitude>0.000003f)throw new Exception("New mode separates posed attachment contact.");
                            contacts++;
                        }
                        probe.SetFloat("_ProbeNormal",1);var normals=sample();probe.SetFloat("_ProbeNormal",0);
                        if(normals.Any(c=>!Finite(RGB(c).magnitude)||Mathf.Abs(RGB(c).magnitude-1)>0.005f))throw new Exception("New mode normal invalid.");
                    }
                }
                report.AppendLine("PASS: added modes 13-15: "+cells+" positive GPU Jacobians, min="+minimum.ToString("G6")+"; "+contacts+" coincident skin/accessory samples in two posed states. Nine transition stages, two thicknesses, two widths; zero identity, floor anchoring and normals checked. Local field tests do not prove collision-free mesh triangles.");
            }
            finally{RenderTexture.active=previous;Object.DestroyImmediate(probe);Object.DestroyImmediate(input);Object.DestroyImmediate(output);Object.DestroyImmediate(rt);}
        }

        static void RenderAddedCases(GameObject root,Camera camera,RenderTexture rt,Texture2D image,StringBuilder report)
        {
            var mats=ShapeChangeSetup.Materials(root);
            var copies=mats.Select(m=>new Material(m)).ToArray();
            float height=mats[0].GetVector("_SCFrame").w;
            var sheet=new Texture2D(image.width*5,image.height*6,TextureFormat.RGB24,false);
            try
            {
                foreach(var m in mats)
                {
                    m.SetVector("_SCBoxAspect",new Vector4(1.6f,0.75f,1.5f,0));m.SetFloat("_SCInflation",2.2f);
                    m.SetFloat("_SCPressSpread",0.5f);m.SetFloat("_SCPanelWidth",0.7f);m.SetFloat("_SCClothBend",0.06f);
                    m.SetFloat("_SCThickness",0.025f);m.SetFloat("_SCUseProxy",1);
                }
                camera.orthographicSize=height*root.transform.lossyScale.y*0.85f;
                for(int row=0;row<6;row++)for(int column=0;column<5;column++)
                {
                    int mode=new[]{1,3,13,14,14,15}[row];
                    ShapeChangeSetup.SetShape(root,mode,column*0.25f,1,1);
                    var look=new Vector3(0,height*(row==2?0.25f:0.58f),0);
                    var offset=new Vector3(row==3||row==4?0:height*0.65f,height*(row==2?2:0.15f),height*(row==4?-3:3));
                    camera.transform.position=root.transform.TransformPoint(look+offset);camera.transform.LookAt(root.transform.TransformPoint(look),root.transform.up);
                    camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,image.width,image.height),0,0);image.Apply();
                    var pixels=image.GetPixels();
                    if(pixels.Count(c=>c.r>0.9f && c.b>0.9f && c.g<0.1f)>pixels.Length/10)throw new Exception("New mode pink render: "+mode);
                    sheet.SetPixels(column*image.width,(5-row)*image.height,image.width,image.height,pixels);
                }
                sheet.Apply();File.WriteAllBytes(Output+"/additional-shapes-sequence.png",sheet.EncodeToPNG());
                camera.orthographicSize=height*root.transform.lossyScale.y;
                foreach(int mode in new[]{13,14,15})
                {
                    ShapeChangeSetup.SetShape(root,mode,1);
                    var target=new Vector3(0,height*(mode==13?0.05f:0.7f),0);
                    camera.transform.position=root.transform.TransformPoint(target+new Vector3(height*3,mode==13?height*0.08f:0,mode==13?0:height*0.25f));
                    camera.transform.LookAt(root.transform.TransformPoint(target),root.transform.up);
                    camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,image.width,image.height),0,0);image.Apply();
                    File.WriteAllBytes(Output+"/added-side-"+mode+".png",image.EncodeToPNG());
                }
                report.AppendLine("Rendered added reference sequences: rows=cuboid/inflation/top-down press/rectangular board front/back/cloth; columns=0,25,50,75,100%. Board/cloth use original topology during deformation plus the optional closed appearance shell at completion.");
            }
            finally{for(int i=0;i<mats.Length;i++){mats[i].CopyPropertiesFromMaterial(copies[i]);Object.DestroyImmediate(copies[i]);}Object.DestroyImmediate(sheet);}
        }
    }
}
