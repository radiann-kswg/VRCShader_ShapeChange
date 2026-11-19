using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace ShapeChange
{
    // A closed surface is necessary because a vertex shader cannot fill the holes
    // between separate legs, open garment hems, or disconnected avatar meshes.
    static class ShapeChangeShell
    {
        public static void Rebake(Animator source, GameObject root)
        {
            var material=ShapeChangeSetup.Materials(root).FirstOrDefault(m=>m.GetFloat("_SCProxy")>0.5f);
            if(!material) throw new InvalidOperationException("補助メッシュがありません。");
            var included=source.GetComponentsInChildren<Renderer>(true).Where(r=>
            {
                string path=AnimationUtility.CalculateTransformPath(r.transform,source.transform);
                var target=root.transform.Find(path)?.GetComponent<Renderer>();
                return target && target.sharedMaterials.Any(m=>m && m.HasProperty("_SCReady") && m.GetFloat("_SCReady")>0 && m.GetFloat("_SCAccessory")<0.5f);
            }).ToArray();
            material.SetTexture("_MainTex",BakeAppearance(source,included,material.GetVector("_SCFrame"),Path.GetDirectoryName(AssetDatabase.GetAssetPath(material)).Replace('\\','/')));
            BakePanels(source,root);
            BakeCube(source,root);
            EditorUtility.SetDirty(material);AssetDatabase.SaveAssets();
        }

        public static void Create(Animator source, GameObject root, Renderer[] included, string folder, Vector4 frame, Vector4 measure)
        {
            var atlas=BakeAppearance(source,included,frame,folder);
            const int columns=128,rows=64;
            var vertices=new List<Vector3>(); var normals=new List<Vector3>(); var triangles=new List<int>();
            var textureUV=new List<Vector2>();
            var channels=Enumerable.Range(0,4).Select(_=>new List<Vector2>()).ToArray();
            for(int y=0;y<=rows;y++) for(int x=0;x<=columns;x++)
            {
                float v=(float)y/rows,u=(float)x/columns;
                float angle=(u-0.5f)*Mathf.PI*2;
                float vertical=-Mathf.Cos(v*Mathf.PI), radius=Mathf.Sin(v*Mathf.PI);
                Vector3 n=new Vector3(Mathf.Sin(angle)*radius,vertical,Mathf.Cos(angle)*radius);
                Vector3 p=new Vector3(n.x*measure.z,(vertical+1)*0.5f,n.z*measure.w);
                vertices.Add(p*frame.w+(Vector3)frame); normals.Add(n);
                textureUV.Add(new Vector2(u,p.y));
                channels[0].Add(new Vector2(p.x,p.y)); channels[1].Add(new Vector2(p.z,0));channels[2].Add(Vector2.zero);
                Vector3 oct=n/(Mathf.Abs(n.x)+Mathf.Abs(n.y)+Mathf.Abs(n.z));
                channels[3].Add(oct.z>=0?new Vector2(oct.x,oct.y):new Vector2((1-Mathf.Abs(oct.y))*(oct.x>=0?1:-1),(1-Mathf.Abs(oct.x))*(oct.y>=0?1:-1)));
                if(y<rows && x<columns)
                {
                    int a=y*(columns+1)+x,b=a+columns+1;
                    triangles.AddRange(new[]{a,a+1,b,a+1,b+1,b});
                }
            }
            var mesh=new Mesh { name="ShapeChange_ClosedSurface" };
            mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,textureUV);mesh.SetTriangles(triangles,0);
            for(int ch=0;ch<4;ch++) mesh.SetUVs(ch+4,channels[ch]);
            mesh.RecalculateTangents();mesh.RecalculateBounds();
            mesh.bindposes=new[]{Matrix4x4.identity};
            mesh.boneWeights=Enumerable.Repeat(new BoneWeight{boneIndex0=0,weight0=1},vertices.Count).ToArray();
            AssetDatabase.CreateAsset(mesh,folder+"/ClosedSurface.asset");
            var shader=Shader.Find("ShapeChange/[Optional] lilToonOpaque");
            if(!shader || !shader.isSupported)shader=Shader.Find(ShapeChangeSetup.StandardShapeShader);
            var material=new Material(shader){name="ClosedSurface"};
            material.SetTexture("_MainTex",atlas);
            if(material.HasProperty("_AsUnlit"))
            {
                material.SetFloat("_AsUnlit",0.2f);material.SetFloat("_Cull",0);
                material.SetFloat("_UseShadow",1);material.SetFloat("_ShadowStrength",0.7f);
                material.SetFloat("_ShadowBlur",0.07f);material.SetColor("_ShadowColor",new Color(0.68f,0.70f,0.78f,1));
            }
            else material.SetFloat("_Glossiness",0);
            material.SetVector("_SCFrame",frame);material.SetVector("_SCMeasure",measure);
            material.SetFloat("_SCReady",1);material.SetFloat("_SCProxy",1);material.SetFloat("_SCUseProxy",1);
            material.SetFloat("_SCFaceStart",0.44f);
            var calibrated=ShapeChangeSetup.Materials(root)[0];
            material.SetVector("_SCHead",calibrated.GetVector("_SCHead"));
            material.SetVector("_SCFaceSize",calibrated.GetVector("_SCFaceSize"));
            material.SetFloat("_SCHeadFront",calibrated.GetFloat("_SCHeadFront"));
            material.SetFloat("_SCPreserveSurface",1);material.SetFloat("_SCFaceCoverage",0.4f);material.SetFloat("_SCSurfaceFollow",0.12f);
            material.SetFloat("_SCBodyBase",calibrated.GetFloat("_SCBodyBase"));
            AssetDatabase.CreateAsset(material,folder+"/ClosedSurface.mat");
            var go=new GameObject("ShapeChange Closed Surface");go.transform.SetParent(root.transform,false);
            var renderer=go.AddComponent<SkinnedMeshRenderer>();renderer.sharedMesh=mesh;renderer.sharedMaterial=material;
            renderer.bones=new[]{root.transform};renderer.rootBone=root.transform;
            renderer.localBounds=new Bounds(new Vector3(0,frame.w*0.6f,0),Vector3.one*frame.w*5);renderer.updateWhenOffscreen=true;
            foreach(var m in ShapeChangeSetup.Materials(root)) m.SetFloat("_SCUseProxy",1);
            BakePanels(source,root);
            BakeCube(source,root);
        }

        internal static void BakeCube(Animator source,GameObject root)
        {
            var material=ShapeChangeSetup.Materials(root).Single(m=>m.GetFloat("_SCProxy")>0.5f);
            string folder=Path.GetDirectoryName(AssetDatabase.GetAssetPath(material)).Replace('\\','/');
            string path=folder+"/CubeAppearance.png";
            var atlas=CaptureCube(source,root,material);
            try{File.WriteAllBytes(path,atlas.EncodeToPNG());}
            finally{Object.DestroyImmediate(atlas);}
            AssetDatabase.ImportAsset(path);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.npotScale=TextureImporterNPOTScale.None;
            importer.wrapMode=TextureWrapMode.Clamp;
            importer.mipmapEnabled=false;
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            material.SetTexture("_SCCubeTex",AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            material.SetFloat("_SCCubeReady",1);
            EditorUtility.SetDirty(material);
        }

        enum CubeCapture { Torso, Arms, Soles }

        internal static Texture2D CaptureCube(Animator source,GameObject root,Material calibration)
        {
            const int tileSize=256,columns=4;
            var atlas=new Texture2D(columns*tileSize,3*tileSize,TextureFormat.RGBA32,false);
            atlas.wrapMode=TextureWrapMode.Clamp;
            var preview=new PreviewRenderUtility();GameObject holder=null,clone=null;
            var colors=new Dictionary<Material,Material>();
            var meshes=new Dictionary<(Mesh,CubeCapture),Mesh>();
            var rt=new RenderTexture(tileSize,tileSize,24,RenderTextureFormat.ARGB32);
            var image=new Texture2D(tileSize,tileSize,TextureFormat.RGBA32,false);
            var previous=RenderTexture.active;bool asyncCompile=ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation=false;
                holder=EditorUtility.CreateGameObjectWithHideFlags("Cube appearance capture",HideFlags.HideAndDontSave);
                holder.SetActive(false);preview.AddSingleGO(holder);
                clone=Object.Instantiate(root,holder.transform,false);
                foreach(var b in clone.GetComponentsInChildren<Behaviour>(true))b.enabled=false;
                clone.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                foreach(var t in clone.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
                var surfaces=new Dictionary<SkinnedMeshRenderer,Mesh>();
                foreach(var r in clone.GetComponentsInChildren<Renderer>(true))
                {
                    var prepared=r.sharedMaterials.Where(m=>m && m.HasProperty("_SCReady") && m.GetFloat("_SCReady")>0.5f).ToArray();
                    if(!(r is SkinnedMeshRenderer skin) || !skin.sharedMesh || prepared.Length==0 ||
                        prepared.Any(m=>m.GetFloat("_SCProxy")>0.5f || m.GetFloat("_SCAccessory")>0.5f || m.GetFloat("_SCAppendage")>0.5f))
                    {
                        r.enabled=false;
                        continue;
                    }
                    skin.updateWhenOffscreen=true;
                    surfaces.Add(skin,skin.sharedMesh);
                    r.sharedMaterials=r.sharedMaterials.Select(m=>
                    {
                        if(!m)return m;
                        if(!colors.TryGetValue(m,out var copy))
                        {
                            copy=CaptureColorMaterial(m);
                            copy.SetFloat("_SCAmount",0);
                            colors.Add(m,copy);
                        }
                        return copy;
                    }).ToArray();
                }
                ShapeChangePose.ApplyPlane(source,clone);holder.SetActive(true);clone.SetActive(true);
                var rig=clone.GetComponent<Animator>();
                var frame=calibration.GetVector("_SCFrame");var measure=calibration.GetVector("_SCMeasure");
                Vector3 Joint(HumanBodyBones id)=>(clone.transform.InverseTransformPoint(rig.GetBoneTransform(id).position)-(Vector3)frame)/frame.w;
                var left=Joint(HumanBodyBones.LeftHand);var right=Joint(HumanBodyBones.RightHand);
                float top=Mathf.Clamp(measure.x+0.025f,0.25f,0.95f);
                float bottom=Mathf.Clamp(Mathf.Min(left.y,right.y)-0.075f,0,top-0.12f);
                float torsoWidth=Mathf.Max(measure.z,Mathf.Max(Mathf.Abs(left.x),Mathf.Abs(right.x))+0.055f);
                float depth=Mathf.Max(measure.w*1.35f,0.11f),capWidth=Mathf.Max(measure.z,0.12f);
                float heelZ=(Joint(HumanBodyBones.LeftFoot).z+Joint(HumanBodyBones.RightFoot).z)*0.5f;
                var camera=preview.camera;camera.enabled=false;camera.cullingMask=1<<31;
                float height=frame.w*clone.transform.lossyScale.y;
                camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;
                camera.nearClipPlane=0.001f;camera.farClipPlane=height*10;camera.targetTexture=rt;
                var directions=new[]{Vector3.forward,Vector3.right,Vector3.back,Vector3.left,Vector3.up,Vector3.down};
                // First six tiles omit limbs. Extra tiles retain arms on front/sides or feet on the bottom.
                var faceByTile=new[]{0,1,2,3,4,5,0,1,3,5};
                Color hem=Color.clear;
                for(int tile=0;tile<faceByTile.Length;tile++)
                {
                    int tileX=tile%columns*tileSize,tileY=tile/columns*tileSize;
                    if(tile==5)
                    {
                        // Close the underside with the outer garment's colour, even under open skirts.
                        atlas.SetPixels(tileX,tileY,tileSize,tileSize,Enumerable.Repeat(hem,tileSize*tileSize).ToArray());
                        continue;
                    }
                    var capture=tile<6?CubeCapture.Torso:tile==9?CubeCapture.Soles:CubeCapture.Arms;
                    foreach(var surface in surfaces)
                    {
                        var key=(surface.Value,capture);
                        if(!meshes.TryGetValue(key,out var mesh))
                        {
                            mesh=CubeCaptureMesh(surface.Value,capture);
                            meshes.Add(key,mesh);
                        }
                        surface.Key.sharedMesh=mesh;
                    }
                    int face=faceByTile[tile];bool cap=face>=4;
                    float halfHeight=cap?depth:(top-bottom)*0.5f;
                    float halfWidth=cap?capWidth:face%2==1?depth:torsoWidth;
                    float centerZ=face==4?calibration.GetVector("_SCHead").w-depth*0.30f:face==5?heelZ+0.02f:0;
                    var center=clone.transform.TransformPoint((Vector3)frame+new Vector3(0,cap?0.5f:(top+bottom)*0.5f,centerZ)*frame.w);
                    camera.orthographicSize=halfHeight*height;camera.aspect=halfWidth/halfHeight;
                    camera.transform.position=center+directions[face]*height*3;
                    camera.transform.LookAt(center,cap?Vector3.forward:Vector3.up);
                    camera.Render();RenderTexture.active=rt;
                    image.ReadPixels(new Rect(0,0,tileSize,tileSize),0,0);image.Apply();
                    var pixels=image.GetPixels();
                    if(tile==2)
                    {
                        var hemPixels=pixels.Take(tileSize*tileSize/4).Where(p=>p.a>=0.5f).ToArray();
                        if(hemPixels.Length>0)
                        {
                            hem=hemPixels.Aggregate(Color.clear,(sum,p)=>sum+p)/hemPixels.Length;
                            hem.a=1;
                        }
                    }
                    if(tile<6)
                    {
                        atlas.SetPixels(tileX,tileY,tileSize,tileSize,ExtendCaptureColors(pixels,tileSize,tileSize,24));
                        continue;
                    }
                    var filled=atlas.GetPixels(face%columns*tileSize,face/columns*tileSize,tileSize,tileSize);
                    for(int i=0;i<pixels.Length;i++)
                        if(pixels[i].a>=0.5f)filled[i]=new Color(pixels[i].r,pixels[i].g,pixels[i].b,1);
                    atlas.SetPixels(tileX,tileY,tileSize,tileSize,filled);
                }
                atlas.Apply();
                return atlas;
            }
            catch{Object.DestroyImmediate(atlas);throw;}
            finally
            {
                RenderTexture.active=previous;ShaderUtil.allowAsyncCompilation=asyncCompile;preview.camera.targetTexture=null;
                if(holder)Object.DestroyImmediate(holder);if(clone)Object.DestroyImmediate(clone);
                foreach(var copy in colors.Values)Object.DestroyImmediate(copy);
                foreach(var mesh in meshes.Values)Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(rt);Object.DestroyImmediate(image);preview.Cleanup();
            }
        }

        static Mesh CubeCaptureMesh(Mesh source,CubeCapture capture)
        {
            var masks=new List<Vector2>();
            source.GetUVs(6,masks);
            if(masks.Count!=source.vertexCount)throw new InvalidOperationException("Cube capture needs prepared limb masks: "+source.name);
            var position=new List<Vector2>();
            if(capture==CubeCapture.Soles)
            {
                source.GetUVs(4,position);
                if(position.Count!=masks.Count)throw new InvalidOperationException("Cube capture needs prepared positions: "+source.name);
            }
            var copy=Object.Instantiate(source);
            try
            {
                for(int submesh=0;submesh<source.subMeshCount;submesh++)
                {
                    if(source.GetTopology(submesh)!=MeshTopology.Triangles)continue;
                    var indices=source.GetTriangles(submesh,false);
                    int offset=(int)source.GetBaseVertex(submesh);
                    var kept=new List<int>(indices.Length);
                    for(int i=0;i<indices.Length;i+=3)
                    {
                        int a=indices[i]+offset,b=indices[i+1]+offset,c=indices[i+2]+offset;
                        Vector2 sum=masks[a]+masks[b]+masks[c];
                        if(capture==CubeCapture.Soles)
                        {
                            if(sum.y<=1.65f || position[a].y+position[b].y+position[c].y>0.42f)continue;
                        }
                        else if(sum.y>1.65f || capture==CubeCapture.Torso && sum.x>1.65f)continue;
                        kept.Add(indices[i]);kept.Add(indices[i+1]);kept.Add(indices[i+2]);
                    }
                    copy.SetTriangles(kept,submesh,false,offset);
                }
                return copy;
            }
            catch{Object.DestroyImmediate(copy);throw;}
        }

        // A frontal and rear capture replace the longitude atlas only in panel
        // modes. The source artwork is the user's avatar, not downloaded art.
        public static void BakePanels(Animator source,GameObject root)
        {
            var material=ShapeChangeSetup.Materials(root).FirstOrDefault(m=>m.GetFloat("_SCProxy")>0.5f);
            if(!material)throw new InvalidOperationException("長方形の外観には補助メッシュが必要です。");
            string folder=Path.GetDirectoryName(AssetDatabase.GetAssetPath(material)).Replace('\\','/');
            var frame=material.GetVector("_SCFrame");const int width=512,height=1024;
            var preview=new PreviewRenderUtility();GameObject holder=null,clone=null;
            var copies=new Dictionary<Material,Material>();
            var rt=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);
            var image=new Texture2D(width,height,TextureFormat.RGBA32,false);
            var atlas=new Texture2D(width*2,height,TextureFormat.RGB24,false);var captures=new Color[2][];
            var previous=RenderTexture.active;bool asyncCompile=ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation=false;
                holder=EditorUtility.CreateGameObjectWithHideFlags("Panel pose capture",HideFlags.HideAndDontSave);holder.SetActive(false);preview.AddSingleGO(holder);
                clone=Object.Instantiate(root,holder.transform,false);
                foreach(var b in clone.GetComponentsInChildren<Behaviour>(true))b.enabled=false;
                clone.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                foreach(var t in clone.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
                foreach(var r in clone.GetComponentsInChildren<Renderer>(true))r.sharedMaterials=r.sharedMaterials.Select(m=>
                {
                    if(!m)return m;
                    if(!copies.ContainsKey(m)){var copy=CaptureColorMaterial(m);copy.SetFloat("_SCAmount",0);copies[m]=copy;}
                    return copies[m];
                }).ToArray();
                ShapeChangePose.ApplyPlane(source,clone);holder.SetActive(true);clone.SetActive(true);
                var camera=preview.camera;camera.enabled=false;camera.cullingMask=1<<31;
                float captureHeight=frame.w*clone.transform.lossyScale.y;
                camera.orthographic=true;camera.orthographicSize=captureHeight*0.55f;camera.aspect=1;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;
                camera.nearClipPlane=0.001f;camera.farClipPlane=captureHeight*10;camera.targetTexture=rt;
                Vector3 center=clone.transform.TransformPoint((Vector3)frame+Vector3.up*frame.w*0.5f);
                for(int side=0;side<2;side++)
                {
                    camera.transform.position=center+(side==0?Vector3.forward:Vector3.back)*captureHeight*3;
                    camera.transform.LookAt(center,Vector3.up);
                    camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();captures[side]=image.GetPixels();
                    atlas.SetPixels(side*width,0,width,height,ExtendCaptureColors(captures[side],width,height,softenGaps:true));
                }
                atlas.Apply();File.WriteAllBytes(folder+"/PanelAppearance.png",atlas.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active=previous;ShaderUtil.allowAsyncCompilation=asyncCompile;preview.camera.targetTexture=null;
                if(holder)Object.DestroyImmediate(holder);if(clone)Object.DestroyImmediate(clone);
                foreach(var copy in copies.Values)Object.DestroyImmediate(copy);
                Object.DestroyImmediate(rt);Object.DestroyImmediate(image);Object.DestroyImmediate(atlas);preview.Cleanup();
            }
            var profile=CreatePanelProfile(captures,width,height);string profilePath=folder+"/PanelProfile.asset";
            var existing=AssetDatabase.LoadAssetAtPath<Texture2D>(profilePath);
            if(existing){EditorUtility.CopySerialized(profile,existing);Object.DestroyImmediate(profile);profile=existing;EditorUtility.SetDirty(profile);}
            else AssetDatabase.CreateAsset(profile,profilePath);
            foreach(var m in ShapeChangeSetup.Materials(root)){m.SetTexture("_SCPanelProfile",profile);EditorUtility.SetDirty(m);}
            string path=folder+"/PanelAppearance.png";AssetDatabase.ImportAsset(path);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.wrapMode=TextureWrapMode.Clamp;
            importer.textureCompression=TextureImporterCompression.Uncompressed;importer.streamingMipmaps=true;importer.SaveAndReimport();
            material.SetTexture("_SCSheetTex",AssetDatabase.LoadAssetAtPath<Texture2D>(path));EditorUtility.SetDirty(material);
        }

        internal static Texture2D CreatePanelProfile(Color[][] captures,int width,int height)
        {
            if(captures==null || captures.Length!=2 || width<1 || height<1 || captures.Any(p=>p==null || p.Length!=(long)width*height))throw new ArgumentException("Invalid panel captures.");
            const int rows=256;var bounds=new Vector2[rows];
            for(int row=0;row<rows;row++)
            {
                float left=float.PositiveInfinity,right=float.NegativeInfinity;
                for(int side=0;side<2;side++)for(int y=row*height/rows;y<Mathf.Max((row+1)*height/rows,row*height/rows+1);y++)for(int x=0;x<width;x++)
                    if(captures[side][Mathf.Min(y,height-1)*width+x].a>=0.5f)
                    {
                        float at=((x+0.5f)/width-0.5f)*1.1f*(side==0?-1:1);
                        left=Mathf.Min(left,at);right=Mathf.Max(right,at);
                    }
                bounds[row]=new Vector2(left,right);
            }
            if(bounds.All(b=>float.IsInfinity(b.x)))throw new InvalidOperationException("Panel capture is empty.");
            for(int row=0;row<rows;row++)if(float.IsInfinity(bounds[row].x))
                for(int distance=1;distance<rows;distance++)
                {
                    int at=row-distance>=0 && !float.IsInfinity(bounds[row-distance].x)?row-distance:row+distance<rows && !float.IsInfinity(bounds[row+distance].x)?row+distance:-1;
                    if(at>=0){bounds[row]=bounds[at];break;}
                }
            // Dilate before averaging so the smooth bounds still contain every captured row.
            var wide=new Vector2[rows];
            for(int row=0;row<rows;row++)
            {
                var b=bounds[row];for(int i=Mathf.Max(0,row-4);i<=Mathf.Min(rows-1,row+4);i++){b.x=Mathf.Min(b.x,bounds[i].x);b.y=Mathf.Max(b.y,bounds[i].y);}wide[row]=b;
            }
            var pixels=new Color[rows];
            for(int row=0;row<rows;row++)
            {
                Vector2 sum=Vector2.zero;int count=0;for(int i=Mathf.Max(0,row-2);i<=Mathf.Min(rows-1,row+2);i++){sum+=wide[i];count++;}
                var b=sum/count;pixels[row]=new Color(b.x-1.1f/width,b.y+1.1f/width,1,1);
            }
            var texture=new Texture2D(1,rows,TextureFormat.RGBAFloat,false,true){name="PanelProfile",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
            texture.SetPixels(pixels);texture.Apply();return texture;
        }

        internal static Color[] ExtendCaptureColors(Color[] pixels,int width,int height,int fadeDistance=0,bool softenGaps=false)
        {
            if(pixels==null || width<1 || height<1 || pixels.Length!=(long)width*height)
                throw new ArgumentException("Invalid panel pixel dimensions.");
            var result=new Color[pixels.Length];
            var nearest=Enumerable.Repeat(-1,pixels.Length).ToArray();
            var queue=new Queue<int>(pixels.Length);
            for(int i=0;i<pixels.Length;i++)if(pixels[i].a>=0.5f){nearest[i]=i;queue.Enqueue(i);}
            // Extend in both image axes. Row-only extension creates stripes across hair and face gaps.
            while(queue.Count>0)
            {
                int at=queue.Dequeue(),x=at%width,y=at/width;
                for(int side=0;side<4;side++)
                {
                    if(side==0 && x==0 || side==1 && x==width-1 || side==2 && y==0 || side==3 && y==height-1)continue;
                    int next=at+(side==0?-1:side==1?1:side==2?-width:width);
                    if(nearest[next]>=0)continue;
                    nearest[next]=nearest[at];queue.Enqueue(next);
                }
            }
            for(int i=0;i<result.Length;i++)
            {
                int source=nearest[i];
                result[i]=source>=0?pixels[source]:new Color(0.2f,0.22f,0.25f);
                int x=i%width,y=i/width;
                int distance=source<0 ? 0 : Mathf.Abs(x-source%width)+Mathf.Abs(y-source/width);
                if(softenGaps && distance>2)
                {
                    int span=Mathf.Min(distance/2,16);
                    var neighbors=pixels[nearest[y*width+Mathf.Max(0,x-span)]]+pixels[nearest[y*width+Mathf.Min(width-1,x+span)]]
                        +pixels[nearest[Mathf.Max(0,y-span)*width+x]]+pixels[nearest[Mathf.Min(height-1,y+span)*width+x]];
                    result[i]=(result[i]+neighbors*0.25f)*0.5f;
                }
                if(fadeDistance<=0)result[i].a=1;
                else result[i].a=source<0?0:1-Mathf.SmoothStep(0,1,distance/(float)fadeDistance);
            }
            return result;
        }

        static Material CaptureColorMaterial(Material material)
        {
            var copy=new Material(material);copy.parent=null;
            if(copy.HasProperty("_AsUnlit"))copy.SetFloat("_AsUnlit",1);
            foreach(string key in new[]{"_UseShadow","_UseReflection","_UseMatCap","_UseMatCap2nd","_UseRim","_UseRimShade"})
                if(copy.HasProperty(key))copy.SetFloat(key,0);
            return copy;
        }

        static Texture2D BakeAppearance(Animator source, Renderer[] included, Vector4 frame, string folder)
        {
            const int views=512,width=192,height=1024;
            var transforms=source.GetComponentsInChildren<Transform>(true);
            var layers=transforms.Select(t=>t.gameObject.layer).ToArray();
            var renderers=source.GetComponentsInChildren<Renderer>(true);
            var enabled=renderers.Select(r=>r.enabled).ToArray();
            var originalMaterials=renderers.Select(r=>r.sharedMaterials).ToArray();
            var captureMaterials=new Dictionary<Material,Material>();
            var cameraObject=new GameObject("ShapeChange appearance capture",typeof(Camera));
            var camera=cameraObject.GetComponent<Camera>();camera.enabled=false;camera.cullingMask=1<<31;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;
            camera.orthographic=true;camera.orthographicSize=frame.w*source.transform.lossyScale.y*0.5f;camera.aspect=(float)width/height;
            camera.nearClipPlane=0.001f;camera.farClipPlane=frame.w*source.transform.lossyScale.y*10;
            var rt=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);
            var image=new Texture2D(width,height,TextureFormat.RGBA32,false);
            var atlas=new Texture2D(views,height,TextureFormat.RGB24,false);
            var result=new Color[views*height];
            var previous=RenderTexture.active;
            bool asyncCompile=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
            try
            {
                foreach(var t in transforms)t.gameObject.layer=31;
                foreach(var r in renderers)if(!included.Contains(r))r.enabled=false;
                // Capture colour without a fixed light direction; the deformed surface supplies the shading.
                foreach(var r in included)r.sharedMaterials=r.sharedMaterials.Select(m=>
                {
                    if(!m)return m;
                    if(!captureMaterials.ContainsKey(m))captureMaterials[m]=CaptureColorMaterial(m);
                    return captureMaterials[m];
                }).ToArray();
                camera.targetTexture=rt;
                Vector3 center=(Vector3)frame+new Vector3(0,frame.w*0.5f,0);
                for(int view=0;view<views;view++)
                {
                    float angle=((view+0.5f)/views-0.5f)*Mathf.PI*2;
                    camera.transform.position=source.transform.TransformPoint(center+new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle))*frame.w*3);
                    camera.transform.LookAt(source.transform.TransformPoint(center),source.transform.up);
                    camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
                    var pixels=image.GetPixels();
                    Color last=new Color(0.2f,0.22f,0.25f,1);
                    for(int y=0;y<height;y++)
                    {
                        // Find the nearest covered pixel for rows where a ray passes between the legs.
                        for(int delta=0;delta<width/2;delta++)
                        {
                            Color right=pixels[y*width+width/2+delta],left=pixels[y*width+width/2-delta];
                            if(right.a>0.5f){last=right;break;}
                            if(left.a>0.5f){last=left;break;}
                        }
                        result[y*views+view]=new Color(last.r,last.g,last.b,1);
                    }
                }
                atlas.SetPixels(result);atlas.Apply();File.WriteAllBytes(folder+"/Appearance.png",atlas.EncodeToPNG());
            }
            finally
            {
                for(int i=0;i<transforms.Length;i++)transforms[i].gameObject.layer=layers[i];
                for(int i=0;i<renderers.Length;i++){renderers[i].enabled=enabled[i];renderers[i].sharedMaterials=originalMaterials[i];}
                foreach(var material in captureMaterials.Values)Object.DestroyImmediate(material);
                ShaderUtil.allowAsyncCompilation=asyncCompile;RenderTexture.active=previous;camera.targetTexture=null;
                Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(rt);Object.DestroyImmediate(image);Object.DestroyImmediate(atlas);
            }
            string path=folder+"/Appearance.png";AssetDatabase.ImportAsset(path);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.wrapModeU=TextureWrapMode.Repeat;importer.wrapModeV=TextureWrapMode.Clamp;
            importer.textureCompression=TextureImporterCompression.Uncompressed;importer.mipmapEnabled=true;importer.streamingMipmaps=true;importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
