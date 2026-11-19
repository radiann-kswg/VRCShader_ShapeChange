using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ShapeChange
{
    public static partial class ShapeChangeValidation
    {
        const string Output = "Outputs/ShapeChangeValidation";
        [MenuItem("Tools/ShapeChange/Validate and Render Preview")]
        public static void Run()
        {
            Directory.CreateDirectory(Output);
            var report = new StringBuilder();
            report.AppendLine("Output: "+Path.GetFullPath(Output));
            bool asyncCompilation=ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation=false;
            try
            {
                var shaderPaths = AssetDatabase.FindAssets("t:Shader",new[]{ShapeChangeSetup.ShaderFolder}).Select(AssetDatabase.GUIDToAssetPath).ToArray();
                foreach(var path in shaderPaths)
                {
                    var shader=AssetDatabase.LoadAssetAtPath<Shader>(path);
                    var errors=ShaderUtil.GetShaderMessages(shader).Where(m=>m.severity.ToString()=="Error").ToArray();
                    if(errors.Length>0) throw new Exception(path+": "+string.Join("; ",errors.Select(e=>e.message)));
                    report.AppendLine("Shader imported: "+shader.name);
                }
                CheckMath(report);
                CheckHeadProjection(report);
                CheckCoinAttachments(report);
                CheckInflatedAttachments(report);
                CheckPosedTangents(report);
                CheckPlaneProjection(report);
                CheckCubeAppearance(report);
                CheckEnvelope(report);
                var avatar=Object.FindObjectsOfType<Animator>().FirstOrDefault(a=>a.name.EndsWith("_ShapeChange"));
                GameObject root;
                if(avatar) root=avatar.gameObject;
                else
                {
                    var source=ShapeChangeSetup.FindAvatar();
                    root=ShapeChangeSetup.Build(source,source.GetComponentsInChildren<Renderer>(true).Where(r=>ShapeChangeSetup.IsAccessory(r.name)).ToArray(),false);
                    if(avatar && avatar.gameObject.scene.path.StartsWith(ShapeChangeSetup.Folder+"/Generated/")) EditorSceneManager.CloseScene(avatar.gameObject.scene,true);
                }
                foreach(var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if(r.sharedMaterials.Any(m=>m && (m.parent || !m.HasProperty("_SCReady") || m.GetFloat("_SCReady")<0.5f)))
                        throw new Exception("Renderer has an unconverted or inherited material: "+r.name);
                    if(!r.sharedMaterials.Any(m=>m && m.HasProperty("_SCReady"))) continue;
                    if(r.rootBone!=root.transform) throw new Exception("Root mismatch: "+r.name);
                    for(int ch=4;ch<8;ch++)
                    {
                        var values=new System.Collections.Generic.List<Vector2>();
                        r.sharedMesh.GetUVs(ch,values);
                        if(values.Count!=r.sharedMesh.vertexCount || values.Any(v=>!Finite(v.x)||!Finite(v.y))) throw new Exception("Invalid baked UV: "+r.name);
                    }
                }
                report.AppendLine("PASS: all prepared renderers share avatar root; baked coordinates/masks/normals are finite.");
                CheckMeshCorrespondence(root,report);
                CheckAddedModes(root,report);
                CheckFlatMapping(root,report);
                CheckReferenceCells(root,report);
                CheckClosedSurface(root,report);
                CheckAccessories(root,report);
                CheckLimbMorph(root,report);
                CheckPoseSizes(root,report);
                Render(root,report);
                CheckSurfaceAreas(root,report,Output);
                foreach(var path in shaderPaths)
                {
                    var shader=AssetDatabase.LoadAssetAtPath<Shader>(path);
                    var errors=ShaderUtil.GetShaderMessages(shader).Where(m=>m.severity.ToString()=="Error").ToArray();
                    if(errors.Length>0) throw new Exception(path+": "+string.Join("; ",errors.Select(e=>e.message)));
                }
                report.AppendLine("PASS: DX11 render completed for Original + 13 shapes + transition/limb cases; no shader compiler errors.");
                ShapeChangeSetup.Open();
                Selection.activeGameObject=root;
            }
            catch(Exception e) { report.AppendLine("FAIL: "+e); Debug.LogException(e); }
            finally { ShaderUtil.allowAsyncCompilation=asyncCompilation; }
            File.WriteAllText(Output+"/validation.txt",report.ToString());
            AssetDatabase.Refresh();
            Debug.Log("[ShapeChange] "+report.ToString());
        }

        static bool Finite(float n) => !float.IsNaN(n) && !float.IsInfinity(n);

        static Animator SourceAvatar(GameObject root)
        {
            string name=root.name.Substring(0,root.name.Length-"_ShapeChange".Length);
            return Object.FindObjectsOfType<Animator>().SingleOrDefault(a=>a.isHuman && a.name==name)
                ?? throw new Exception("Load exactly one original avatar named "+name+" for validation.");
        }

        static void CheckMeshCorrespondence(GameObject root,StringBuilder report)
        {
            var source=SourceAvatar(root);int meshes=0,vertices=0,faces=0;
            foreach(var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if(!r.sharedMaterials.Any(m=>m && m.HasProperty("_SCReady") && m.GetFloat("_SCReady")>0.5f && m.GetFloat("_SCProxy")<0.5f))continue;
                string path=AnimationUtility.CalculateTransformPath(r.transform,root.transform);
                var originalTransform=string.IsNullOrEmpty(path)?source.transform:source.transform.Find(path);
                var originalSkin=originalTransform?originalTransform.GetComponent<SkinnedMeshRenderer>():null;
                var originalFilter=originalTransform?originalTransform.GetComponent<MeshFilter>():null;
                var before=originalSkin?originalSkin.sharedMesh:originalFilter?originalFilter.sharedMesh:null;
                var after=r.sharedMesh;
                if(!before || !after || before.vertexCount!=after.vertexCount || before.subMeshCount!=after.subMeshCount || !before.vertices.SequenceEqual(after.vertices))
                    throw new Exception("Source vertex correspondence changed: "+path);
                for(int sub=0;sub<before.subMeshCount;sub++)
                {
                    var indices=before.GetIndices(sub);
                    if(before.GetTopology(sub)!=after.GetTopology(sub) || !indices.SequenceEqual(after.GetIndices(sub)))
                        throw new Exception("Source face connectivity changed: "+path);
                    if(before.GetTopology(sub)==MeshTopology.Triangles)faces+=indices.Length/3;
                }
                for(int channel=0;channel<4;channel++)
                {
                    var a=new System.Collections.Generic.List<Vector4>();var b=new System.Collections.Generic.List<Vector4>();
                    before.GetUVs(channel,a);after.GetUVs(channel,b);
                    if(!a.SequenceEqual(b))throw new Exception("Original UV changed: "+path+" / "+channel);
                }
                if(before.blendShapeCount!=after.blendShapeCount)throw new Exception("Blend-shape count changed: "+path);
                meshes++;vertices+=after.vertexCount;
            }
            if(meshes==0)throw new Exception("No source meshes were checked.");
            report.AppendLine("PASS: "+meshes+" source meshes / "+vertices+" vertices / "+faces+" triangles retain vertex order, rest positions, submesh index connectivity and UV0-3. This is connectivity preservation, not an intersection-free or area-preserving deformation guarantee.");
        }

        static void CheckEnvelope(StringBuilder report)
        {
            var probe=new Material(Shader.Find("Hidden/ShapeChange/Validation"));
            const int sampleCount=26*5*7;
            var input=new Texture2D(sampleCount,1,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point};
            var output=new Texture2D(sampleCount,1,TextureFormat.RGBAFloat,false,true);
            var rt=new RenderTexture(sampleCount,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var previous=RenderTexture.active;int checks=0;
            try
            {
                probe.SetVector("_SCBoxAspect",Vector4.one);probe.SetFloat("_SCInflation",1);
                probe.SetFloat("_ProbeEnvelope",1);probe.SetFloat("_SCSize",1);probe.SetFloat("_SCThickness",0.025f);probe.SetFloat("_SCLayer",0.025f);
                probe.SetFloat("_SCPanelBottom",0.25f);probe.SetVector("_SCMeasure",new Vector4(0.8f,0.2f,0.15f,0.08f));
                probe.SetVector("_SCHead",new Vector4(0,0.9f,0.075f,0));probe.SetFloat("_SCFaceCoverage",0.4f);
                for(int mode=0;mode<13;mode++)
                {
                    probe.SetFloat("_SCMode",mode);float top=mode==2 || mode==5?0.8f:1;
                    var colors=new System.Collections.Generic.List<Color>();
                    for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)for(int z=-1;z<=1;z++)
                    {
                        if(x==0 && y==0 && z==0)continue;
                        foreach(float radius in new[]{0.3f,0.6f,1f,2f,4f})
                        {
                            var q=new Vector3(x,y,z).normalized*radius;
                            var p=Vector3.Scale(q,new Vector3(0.15f,(top-0.25f)*0.5f,0.08f))+new Vector3(0,(top+0.25f)*0.5f,0);
                            foreach(var d in new[]{Vector3.zero,Vector3.right,Vector3.left,Vector3.up,Vector3.down,Vector3.forward,Vector3.back})
                            {var v=p+d*0.001f;colors.Add(new Color(v.x,v.y,v.z,1));}
                        }
                    }
                    input.SetPixels(colors.ToArray());input.Apply();
                    foreach(float amount in new[]{0.25f,0.5f,0.75f,1f})
                    {
                        probe.SetFloat("_SCAmount",amount);Graphics.Blit(input,rt,probe);RenderTexture.active=rt;
                        output.ReadPixels(new Rect(0,0,sampleCount,1),0,0);output.Apply();var values=output.GetPixels();
                        for(int i=0;i<values.Length;i+=7)
                        {
                            // Central differences resolve tight corners without one-sided curvature bias or float cancellation.
                            var dx=(RGB(values[i+1])-RGB(values[i+2]))*500;
                            var dy=(RGB(values[i+3])-RGB(values[i+4]))*500;
                            var dz=(RGB(values[i+5])-RGB(values[i+6]))*500;
                            float determinant=Vector3.Dot(dx,Vector3.Cross(dy,dz));
                            if(!Finite(determinant) || determinant<=0)throw new Exception("Envelope locally folded: mode="+mode+" amount="+amount+" sample="+i+" determinant="+determinant);
                            if((mode==2 || mode==5) && Mathf.Abs(values[i].a*0.2f/(mode==2?0.56f:0.5f)-0.4f)>0.00001f)throw new Exception("Headed shape is not 2.5 heads high.");
                            checks++;
                        }
                    }
                }
                report.AppendLine("PASS: "+checks+" GPU envelope Jacobians preserve orientation (13 shapes, 26 directions, 5 radii, 4 blend amounts); headed proportions = 2.5 heads. This checks the shared cage, not all triangles at protected-face/limb joins.");
            }
            finally{RenderTexture.active=previous;Object.DestroyImmediate(probe);Object.DestroyImmediate(input);Object.DestroyImmediate(output);Object.DestroyImmediate(rt);}
        }

        static Vector3 FlatScale(Material mat,int mode)
        {
            float size=Mathf.Max(mat.GetFloat("_SCSize"),0.05f);
            return new Vector3((mode==11?0.52f:mode==12?1.15f:1)*size,size,
                Mathf.Max(mat.GetFloat("_SCThickness"),0.004f)*size/Mathf.Max(2*mat.GetVector("_SCMeasure").w,0.08f));
        }

        static void CheckReferenceCells(GameObject root,StringBuilder report)
        {
            var mat=ShapeChangeSetup.Materials(root).First(m=>m.GetFloat("_SCProxy")<0.5f);
            var probe=new Material(Shader.Find("Hidden/ShapeChange/Validation"));
            var samples=new System.Collections.Generic.List<Color>();
            foreach(float x in new[]{-0.15f,0f,0.15f})foreach(float y in new[]{-0.01f,0f,0.2f,0.45f,0.65f,0.76f,0.85f,1f})foreach(float z in new[]{-0.12f,0f,0.12f})
                foreach(var d in new[]{Vector3.zero,Vector3.right,Vector3.up,Vector3.forward})
                {var p=new Vector3(x,y,z)+d*0.0001f;samples.Add(new Color(p.x,p.y,p.z,1));}
            var input=new Texture2D(samples.Count,1,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point};
            var output=new Texture2D(samples.Count,1,TextureFormat.RGBAFloat,false,true);
            var rt=new RenderTexture(samples.Count,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);var previous=RenderTexture.active;int checks=0;
            try
            {
                foreach(string key in new[]{"_SCFrame","_SCMeasure","_SCHead"})probe.SetVector(key,mat.GetVector(key));
                foreach(string key in new[]{"_SCSize","_SCThickness","_SCLayer","_SCFaceCoverage"})probe.SetFloat(key,mat.GetFloat(key));
                CopyPosture(mat,probe);probe.SetFloat("_ProbeReference",1);probe.SetFloat("_SCPreserveSurface",1);probe.SetFloat("_SCSmoothEnvelope",1);
                probe.SetFloat("_SCArms",1);probe.SetFloat("_SCLegs",1);input.SetPixels(samples.ToArray());input.Apply();
                foreach(int mode in new[]{0,1,2,3,4,5,7})foreach(float amount in new[]{0.25f,0.5f,0.75f,1f})foreach(var mask in new[]{Vector3.zero,Vector3.up,Vector3.forward})
                {
                    probe.SetFloat("_SCMode",mode);probe.SetFloat("_SCAmount",amount);probe.SetVector("_ProbeAnchorMasks",mask);
                    Graphics.Blit(input,rt,probe);RenderTexture.active=rt;output.ReadPixels(new Rect(0,0,samples.Count,1),0,0);output.Apply();var values=output.GetPixels();
                    for(int i=0;i<values.Length;i+=4)
                    {
                        var p=RGB(values[i]);var dx=(RGB(values[i+1])-p)*10000;var dy=(RGB(values[i+2])-p)*10000;var dz=(RGB(values[i+3])-p)*10000;
                        float determinant=Vector3.Dot(dx,Vector3.Cross(dy,dz));
                        if(!Finite(determinant) || determinant<=0)throw new Exception("Ordered body sections folded: mode="+mode+" amount="+amount+" sample="+i+" determinant="+determinant);
                        // Short inflation legs lift the body; limb-tagged grid points are not floor contacts.
                        float lift=mode==3 && probe.GetFloat("_SCRigReady")>0.5f?
                            Mathf.Lerp(Mathf.Max((probe.GetVector("_SCLegL").y+probe.GetVector("_SCLegR").y)*0.5f*Mathf.Max(probe.GetFloat("_SCSize"),0.05f)*0.25f-ShapeChangePose.HalfSize(probe).y*0.08f,0),0.03f,Mathf.Clamp01(probe.GetFloat("_SCSeated"))):0;
                        if(samples[i].g==0 && (mode!=3 || mask==Vector3.zero) && Mathf.Abs(p.y-lift*amount)>0.00001f)throw new Exception("Body base differs from the kept-leg lift: "+mode);
                        checks++;
                    }
                }
                report.AppendLine("PASS: "+checks+" ordered-section GPU cells retain positive orientation across seven volume modes and four blend amounts with body/arm/leg masks. Varying-weight joins and triangle intersections are separate from this local field check.");
            }
            finally{RenderTexture.active=previous;Object.DestroyImmediate(probe);Object.DestroyImmediate(input);Object.DestroyImmediate(output);Object.DestroyImmediate(rt);}
        }

        static void CheckFlatMapping(GameObject root,StringBuilder report)
        {
            var mat=ShapeChangeSetup.Materials(root).First(m=>m.GetFloat("_SCProxy")<0.5f);
            var probe=new Material(Shader.Find("Hidden/ShapeChange/Validation"));
            var input=new Texture2D(128,1,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point};
            var output=new Texture2D(128,1,TextureFormat.RGBAFloat,false,true);
            var rt=new RenderTexture(128,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var previous=RenderTexture.active;int checks=0;
            try
            {
                foreach(string key in new[]{"_SCFrame","_SCMeasure","_SCHead"})probe.SetVector(key,mat.GetVector(key));
                foreach(string key in new[]{"_SCSize","_SCThickness","_SCLayer"})probe.SetFloat(key,mat.GetFloat(key));
                probe.SetFloat("_SCReady",1);probe.SetFloat("_SCEnabled",1);probe.SetFloat("_SCUseProxy",1);
                var random=new System.Random(41);
                var points=Enumerable.Range(0,128).Select(i=>new Color((float)random.NextDouble()-0.5f,(float)random.NextDouble()*1.2f-0.1f,(float)random.NextDouble()*0.4f-0.2f,i%4)).ToArray();
                input.SetPixels(points);input.Apply();var frame=mat.GetVector("_SCFrame");float size=Mathf.Max(mat.GetFloat("_SCSize"),0.05f);
                var poses=new[]{Matrix4x4.identity,Matrix4x4.TRS(new Vector3(0.04f,-0.02f,0.03f),Quaternion.Euler(13,27,-9),Vector3.one)};
                probe.SetFloat("_ProbeSkinned",1);
                foreach(var pose in poses)for(int accessory=0;accessory<2;accessory++)for(int mode=9;mode<13;mode++)foreach(float amount in new[]{0f,0.25f,0.5f,0.75f,1f})
                {
                    probe.SetMatrix("_ProbePose",pose);probe.SetFloat("_SCAccessory",accessory);
                    probe.SetFloat("_SCMode",mode);probe.SetFloat("_SCAmount",amount);
                    Graphics.Blit(input,rt,probe);RenderTexture.active=rt;output.ReadPixels(new Rect(0,0,128,1),0,0);output.Apply();
                    var values=output.GetPixels();var scale=Vector3.Lerp(Vector3.one,FlatScale(mat,mode),amount);
                    if(scale.x*scale.y*scale.z<=0)throw new Exception("Flat map loses thickness/orientation.");
                    var rotation=Quaternion.Euler(mode==9?-90*amount:0,0,0);
                    var center=Vector3.Lerp(new Vector3(0,0.5f,0),new Vector3(0,mode==9?Mathf.Max(mat.GetFloat("_SCThickness"),0.004f)*size*0.5f:size*0.5f,0),amount);
                    for(int i=0;i<points.Length;i++)
                    {
                        var posed=(pose.MultiplyPoint3x4(RGB(points[i])*frame.w+(Vector3)frame)-(Vector3)frame)/frame.w;
                        var projected=Vector3.Scale(posed-new Vector3(0,0.5f,0),scale);
                        float soft=Mathf.Max(mat.GetVector("_SCMeasure").w,0.025f)*Mathf.Clamp(mat.GetFloat("_SCLayer")*4,0.08f,0.4f);
                        projected.z=Mathf.Lerp(posed.z,Mathf.Max(mat.GetFloat("_SCThickness"),0.004f)*size*0.5f*posed.z/(Mathf.Abs(posed.z)+soft),amount);
                        var expected=(rotation*projected+center)*frame.w+(Vector3)frame;
                        if((RGB(values[i])-expected).magnitude>frame.w*0.000003f)throw new Exception("Flat silhouette correspondence changed: mode="+mode+" amount="+amount);
                        checks++;
                    }
                }
                report.AppendLine("PASS: "+checks+" flat GPU samples (four sheet modes, five blend values, body/accessory and two skinned poses) match the common bounded, strictly increasing plane projection. Shared skinned contact points remain coincident; triangle intersections are a separate check.");
            }
            finally{RenderTexture.active=previous;Object.DestroyImmediate(probe);Object.DestroyImmediate(input);Object.DestroyImmediate(output);Object.DestroyImmediate(rt);}
        }

        static void CheckClosedSurface(GameObject root,StringBuilder report)
        {
            var surface=root.transform.Find("ShapeChange Closed Surface");
            if(!surface){report.AppendLine("SKIP: optional closed surface was not generated.");return;}
            var mesh=surface.GetComponent<SkinnedMeshRenderer>().sharedMesh;
            var welds=new System.Collections.Generic.Dictionary<Vector3Int,int>();
            int[] ids=new int[mesh.vertexCount];var vertices=mesh.vertices;
            float precision=100000f/Mathf.Max(mesh.bounds.size.y,0.0001f);
            for(int i=0;i<vertices.Length;i++)
            {
                var p=Vector3Int.RoundToInt(vertices[i]*precision);
                if(!welds.TryGetValue(p,out var id)){ id=welds.Count;welds[p]=id; }
                ids[i]=id;
            }
            var edges=new System.Collections.Generic.Dictionary<long,int>();var indices=mesh.triangles;
            for(int i=0;i<indices.Length;i+=3)
            {
                int a=ids[indices[i]],b=ids[indices[i+1]],c=ids[indices[i+2]];
                if(a==b || b==c || c==a)continue;
                foreach(var pair in new[]{(a,b),(b,c),(c,a)})
                {
                    long key=((long)Mathf.Min(pair.Item1,pair.Item2)<<32)|(uint)Mathf.Max(pair.Item1,pair.Item2);
                    edges.TryGetValue(key,out int count);edges[key]=count+1;
                }
            }
            if(edges.Values.Any(count=>count!=2))throw new Exception("Closed surface has boundary/non-manifold edges");
            report.AppendLine("PASS: closed surface has no geometric boundary edges (UV seam/poles welded for validation).");
        }

        static void CheckMath(StringBuilder report)
        {
            var shader=Shader.Find("Hidden/ShapeChange/Validation");
            if(!shader) throw new Exception("Validation shader missing");
            var material=new Material(shader);
            var points=new[]{ Vector3.zero,new Vector3(0,0.5f,0),new Vector3(0,1,0),new Vector3(0.15f,0.5f,0.08f),new Vector3(-0.6f,0.8f,0),new Vector3(0.6f,0.8f,0),new Vector3(0,0.5f,-0.4f),new Vector3(0,0.8f,0.06f) };
            var input=new Texture2D(32,1,TextureFormat.RGBAFloat,false,true) { filterMode=FilterMode.Point };
            var colors=new Color[32];
            for(int i=0;i<32;i++) { var p=points[i%8]; colors[i]=new Color(p.x,p.y,p.z,i/8); }
            input.SetPixels(colors);input.Apply();
            var output=new Texture2D(32,1,TextureFormat.RGBAFloat,false,true);
            var rt=new RenderTexture(32,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var previous=RenderTexture.active;
            material.SetVector("_SCBoxAspect",Vector4.one);material.SetFloat("_SCInflation",1);
            material.SetFloat("_SCEnabled",1);material.SetFloat("_SCReady",1);material.SetFloat("_SCSize",1);
            material.SetFloat("_SCThickness",0);material.SetFloat("_SCLayer",0.025f);material.SetFloat("_SCNormalBlend",1);
            material.SetFloat("_SCFaceStart",0.44f);material.SetFloat("_SCBodyBase",0.45f);
            material.SetFloat("_SCPreserveSurface",1);
            material.SetFloat("_SCSurfaceFollow",0.12f);
            material.SetFloat("_SCSmoothEnvelope",1);material.SetFloat("_SCPanelBottom",0.25f);
            material.SetFloat("_SCFaceCoverage",0.4f);material.SetVector("_SCHead",new Vector4(0,0.9f,0.075f,0));
            material.SetVector("_SCFrame",new Vector4(0.1f,-0.05f,0.2f,1.35f));material.SetVector("_SCMeasure",new Vector4(0.8f,0.2f,0.15f,0.08f));
            int checkedVertices=0;
            Color[] checkedNormals=null;
            try
            {
                for(int posture=0;posture<2;posture++) for(int preservation=0;preservation<2;preservation++) for(int proxy=0;proxy<2;proxy++) for(int mode=0;mode<13;mode++) for(int arms=0;arms<3;arms++) for(int legs=0;legs<3;legs++)
                foreach(float amount in new[]{0f,0.5f,1f}) for(int normal=0;normal<3;normal++)
                {
                    material.SetFloat("_SCMode",mode);material.SetFloat("_SCArms",arms);material.SetFloat("_SCLegs",legs);
                    material.SetFloat("_SCAmount",amount);material.SetFloat("_ProbeNormal",normal);
                    material.SetFloat("_SCProxy",proxy);
                    material.SetFloat("_SCPreserveSurface",preservation);
                    material.SetFloat("_SCUpright",posture);material.SetFloat("_SCSeated",posture);material.SetFloat("_SCHeadFront",0.09f);
                    material.SetFloat("_SCRigReady",1);material.SetVector("_SCArmL",new Vector4(-0.12f,0.76f,0,0));material.SetVector("_SCArmR",new Vector4(0.12f,0.76f,0,0));
                    material.SetVector("_SCLegL",new Vector4(-0.06f,0.55f,0,0));material.SetVector("_SCLegR",new Vector4(0.06f,0.55f,0,0));
                    Graphics.Blit(input,rt,material);RenderTexture.active=rt;
                    output.ReadPixels(new Rect(0,0,32,1),0,0);output.Apply();
                    var values=output.GetPixels();
                    if(normal==1)checkedNormals=values;
                    for(int i=0;i<values.Length;i++)
                    {
                        var c=values[i];var v=new Vector3(c.r,c.g,c.b);
                        if(!Finite(v.x)||!Finite(v.y)||!Finite(v.z)) throw new Exception("Non-finite GPU result: "+mode);
                        if(normal>0 && Mathf.Abs(v.magnitude-1)>0.005f) throw new Exception("Invalid normal/tangent: "+mode);
                        if(normal==2 && Mathf.Abs(Vector3.Dot(v,RGB(checkedNormals[i])))>0.005f)throw new Exception("UV tangent is not perpendicular to the deformed normal: "+mode);
                        if(normal==0 && amount==0 && Vector3.Distance(v,points[i%8]*1.35f+new Vector3(0.1f,-0.05f,0.2f))>0.0001f) throw new Exception("Zero amount changes vertices: index="+i+" actual="+v.ToString("F6")+" expected="+(points[i%8]*1.35f+new Vector3(0.1f,-0.05f,0.2f)).ToString("F6"));
                        checkedVertices++;
                    }
                }
                material.SetFloat("_SCAppendage",1);material.SetFloat("_SCProxy",0);material.SetFloat("_SCUseProxy",1);
                material.SetFloat("_SCArms",1);material.SetFloat("_SCLegs",1);material.SetFloat("_ProbeNormal",0);material.SetFloat("_ProbeFade",1);
                for(int mode=0;mode<13;mode++)foreach(float amount in new[]{0f,0.5f,1f})
                {
                    material.SetFloat("_SCMode",mode);material.SetFloat("_SCAmount",amount);
                    Graphics.Blit(input,rt,material);RenderTexture.active=rt;output.ReadPixels(new Rect(0,0,32,1),0,0);output.Apply();
                    foreach(var value in output.GetPixels())
                    {
                        float start=mode<=2 || mode==6 || mode==7?0.45f:0.65f;
                        float fade=mode<9?Mathf.SmoothStep(0,1,Mathf.InverseLerp(start,1,amount)):0;
                        if(!Finite(value.r)||!Finite(value.g)||!Finite(value.b)||Mathf.Abs(value.a-fade)>0.00001f)
                            throw new Exception("Integrated ears/tail retain a source protrusion or invalid position: "+mode);
                    }
                }
                report.AppendLine("PASS: integrated ears/tail have finite positions; volume modes hand over to the enabled proxy, sheet modes retain the original surfaces (1,248 GPU samples).");
            }
            finally { RenderTexture.active=previous;Object.DestroyImmediate(material);Object.DestroyImmediate(input);Object.DestroyImmediate(output);Object.DestroyImmediate(rt); }
            report.AppendLine("PASS: "+checkedVertices+" GPU position/normal/tangent checks (upright/legacy, seated/standing anchors, preservation on/off, original + proxy, 13 modes, 9 limb combinations, 0/0.5/1 blend); zero blend preserves positions, UV tangents stay perpendicular to normals.");
        }

        static void CheckPosedTangents(StringBuilder report)
        {
            var probe=new Material(Shader.Find("Hidden/ShapeChange/Validation"));
            var input=new Texture2D(4,1,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point};var read=new Texture2D(4,1,TextureFormat.RGBAFloat,false,true);
            var rt=new RenderTexture(4,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);var previous=RenderTexture.active;bool async=ShaderUtil.allowAsyncCompilation;
            int checks=0,failures=0;var details=new System.Text.StringBuilder();var modes=new int[16];float worst=0;
            Func<Color,Vector3> vec=c=>new Vector3(c.r,c.g,c.b);
            Func<Color[]> sample=()=>{Graphics.Blit(input,rt,probe);RenderTexture.active=rt;read.ReadPixels(new Rect(0,0,4,1),0,0);read.Apply();return read.GetPixels();};
            try{
             ShaderUtil.allowAsyncCompilation=false;
             probe.SetVector("_SCFrame",new Vector4(0,0,0,1));probe.SetVector("_SCMeasure",new Vector4(.8f,.2f,.15f,.08f));probe.SetVector("_SCHead",new Vector4(0,.88f,.075f,0));probe.SetVector("_SCFaceSize",new Vector4(.15f,.2f,.78f,.98f));
             probe.SetVector("_SCArmL",new Vector4(.18f,.7f,0,0));probe.SetVector("_SCArmR",new Vector4(-.18f,.7f,0,0));probe.SetVector("_SCLegL",new Vector4(.08f,.45f,0,0));probe.SetVector("_SCLegR",new Vector4(-.08f,.45f,0,0));
             foreach(string key in new[]{"_SCEnabled","_SCReady","_SCPreserveSurface","_SCSmoothEnvelope","_SCUpright","_SCRigReady","_SCNormalBlend","_SCSize","_SCInflation"})probe.SetFloat(key,1);
             probe.SetFloat("_SCHeadFront",.09f);probe.SetFloat("_SCLayer",.025f);probe.SetFloat("_SCFaceCoverage",.4f);probe.SetFloat("_SCThickness",.02f);probe.SetFloat("_SCPanelWidth",.7f);probe.SetFloat("_SCPressSpread",.5f);probe.SetFloat("_SCClothBend",.06f);
             probe.SetFloat("_ProbeSkinned",1);probe.SetFloat("_ProbePrepared",1);probe.SetVector("_ProbeRestNormal",Vector3.forward);
             var rotations=new[]{Quaternion.identity,Quaternion.Euler(23,47,-19),Quaternion.Euler(-28,71,38)};
             var points=new[]{new Vector3(.012f,.9f,.06f),new Vector3(.4f,.65f,.02f),new Vector3(.08f,.2f,.03f)};
             foreach(int mode in Enumerable.Range(0,16))foreach(float amount in new[]{0f,.25f,.5f,.75f,1f})foreach(var rotation in rotations)for(int region=0;region<3;region++){
              probe.SetFloat("_SCMode",mode);probe.SetFloat("_SCAmount",amount);probe.SetFloat("_SCArms",mode==3?1:0);probe.SetFloat("_SCLegs",mode==3?1:0);
              var pivot=points[region];probe.SetMatrix("_ProbePose",Matrix4x4.TRS(pivot-rotation*pivot,rotation,Vector3.one));
              var tangent=new Vector3(.6f,.8f,0);probe.SetVector("_ProbeRestTangent",tangent);
              var p=points[region];float mask=region+1,epsilon=.0005f;
              var samples=new[]{p,p+tangent*epsilon,p-tangent*epsilon,p};input.SetPixels(samples.Select(v=>new Color(v.x,v.y,v.z,mask)).ToArray());input.Apply();
              probe.SetFloat("_ProbeNormal",0);var positions=sample();probe.SetFloat("_ProbeNormal",1);var n=vec(sample()[0]).normalized;probe.SetFloat("_ProbeNormal",2);var actual=vec(sample()[0]).normalized;
              var delta=vec(positions[1])-vec(positions[2]);var expected=(delta-n*Vector3.Dot(n,delta)).normalized;
              float angle=Vector3.Angle(actual,expected);worst=Mathf.Max(worst,angle);checks++;
              if(float.IsNaN(angle)||delta.sqrMagnitude<1e-16f||angle>.35f){failures++;modes[mode]++;if(failures<=32)details.AppendLine("mode="+mode+" amount="+amount+" region="+region+" pose="+rotation.eulerAngles+" tangentAngle="+angle);}
             }
             if(failures>0)throw new Exception("Posed UV tangents differ from the actual surface: "+failures+" / "+checks+"; worstDegrees="+worst+"\n"+details);
             report.AppendLine("PASS: "+checks+" posed UV tangents agree with surface differentials across all 16 modes, 5 stages, 3 poses and head/arm/leg regions; worstDegrees="+worst+".");
            }finally{RenderTexture.active=previous;ShaderUtil.allowAsyncCompilation=async;Object.DestroyImmediate(probe);Object.DestroyImmediate(input);Object.DestroyImmediate(read);rt.Release();Object.DestroyImmediate(rt);}
        }

        static void CheckPlaneProjection(StringBuilder report)
        {
            const int width=16,height=32,count=32;
            var captures=new[]{new Color[width*height],new Color[width*height]};
            for(int side=0;side<2;side++)for(int y=0;y<height;y++)for(int x=5-side;x<=10+side;x++)captures[side][y*width+x]=Color.white;
            var profile=ShapeChangeShell.CreatePanelProfile(captures,width,height);
            var probe=new Material(Shader.Find("Hidden/ShapeChange/Validation"));
            var input=new Texture2D(count,1,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point};var read=new Texture2D(count,1,TextureFormat.RGBAFloat,false,true);
            var rt=new RenderTexture(count,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);var previous=RenderTexture.active;
            int checks=0;Action<bool,string> require=(ok,message)=>{checks++;if(!ok)throw new Exception(message);};
            Func<Color[]> sample=()=>{Graphics.Blit(input,rt,probe);RenderTexture.active=rt;read.ReadPixels(new Rect(0,0,count,1),0,0);read.Apply();return read.GetPixels();};
            try
            {
                require(profile.width==1 && profile.height==256,"Invalid row profile dimensions.");
                require(profile.GetPixels().All(c=>c.r<c.g && c.b==1 && c.r<=-0.2f && c.g>=0.2f),"Row profile lost captured bounds.");
                probe.SetTexture("_SCPanelProfile",profile);probe.SetVector("_SCFrame",new Vector4(0,0,0,1));probe.SetVector("_SCMeasure",new Vector4(.8f,.2f,.15f,.08f));probe.SetVector("_SCHead",new Vector4(0,.88f,.075f,0));probe.SetVector("_SCFaceSize",new Vector4(.15f,.2f,.78f,.98f));
                foreach(string key in new[]{"_SCEnabled","_SCReady","_SCUpright","_SCSmoothEnvelope","_SCPreserveSurface","_SCNormalBlend"})probe.SetFloat(key,1);
                probe.SetFloat("_SCLayer",.025f);probe.SetFloat("_SCPanelWidth",.7f);probe.SetFloat("_SCClothBend",.06f);probe.SetFloat("_ProbeReference",1);
                foreach(float size in new[]{.2f,1f,2f})foreach(float thickness in new[]{.004f,.02f,.08f})foreach(int mode in new[]{9,10,11,12})
                {
                    probe.SetFloat("_SCMode",mode);probe.SetFloat("_SCSize",size);probe.SetFloat("_SCThickness",thickness);probe.SetFloat("_SCAmount",1);
                    input.SetPixels(Enumerable.Range(0,count).Select(i=>new Color(.02f,.5f,-.5f+i/(float)(count-1),0)).ToArray());input.Apply();var values=sample();
                    float old=float.NegativeInfinity;
                    foreach(var value in values){float depth=mode==9?value.g-thickness*size*.5f:value.b;require(Mathf.Abs(depth)<=thickness*size*.5001f,"Sheet exceeds uniform thickness.");require(depth>old,"Sheet swaps depth order.");old=depth;}
                }
                // Different widths and centers at the feet, torso, neck, face and topmost tip.
                var rows=new[]{new Color(-.15f,.09f,1,1),new Color(-.25f,.23f,1,1),new Color(-.12f,.08f,1,1),
                    new Color(-.07f,.13f,1,1),new Color(.06f,.09f,1,1)};
                profile.SetPixels(Enumerable.Range(0,256).Select(i=>rows[i<44?0:i<160?1:i<192?2:i<232?3:4]).ToArray());profile.Apply();
                var heights=new[]{.1f,.4f,.7f,.86f,1f};
                foreach(int mode in new[]{14,15})foreach(float size in new[]{.2f,1f,2f})
                {
                    probe.SetFloat("_SCMode",mode);probe.SetFloat("_SCSize",size);probe.SetFloat("_SCThickness",.02f);
                    probe.SetFloat("_ProbeReference",1);probe.SetFloat("_ProbePanelInverse",0);
                    input.SetPixels(Enumerable.Range(0,count).Select(i=>
                    {
                        int region=(i/2)%rows.Length;
                        return new Color(i%2==0?rows[region].r:rows[region].g,heights[region],.06f,0);
                    }).ToArray());input.Apply();
                    foreach(float amount in new[]{0f,.5f,1f})
                    {
                        probe.SetFloat("_SCAmount",amount);var projected=sample();
                        for(int region=0;region<rows.Length;region++)
                        {
                            float sourceSpan=rows[region].g-rows[region].r;
                            float expected=Mathf.Lerp(sourceSpan,.7f*size*Mathf.Min(1,sourceSpan/.15f),amount);
                            float actual=projected[region*2+1].r-projected[region*2].r;
                            require(Mathf.Abs(actual-expected)<.00003f,$"Mode{mode} width at y={heights[region]}, Size={size}, Amount={amount}: {actual} expected {expected}.");
                        }
                    }
                    probe.SetFloat("_ProbeReference",0);probe.SetFloat("_ProbePanelInverse",1);
                    var points=Enumerable.Range(0,count).Select(i=>new Color((i%4-1.5f)*.03f,.05f+i/(float)(count-1),.06f,0)).ToArray();input.SetPixels(points);input.Apply();var values=sample();
                    for(int i=0;i<count;i++){require(Mathf.Abs(values[i].r-points[i].r)<.00003f && Mathf.Abs(values[i].g-points[i].g)<.00003f,"Panel geometry and colour inverse differ.");require(Mathf.Abs(values[i].b)<=.01001f*size,"Panel depth invalid.");}
                }
                report.AppendLine("PASS: "+checks+" bounded plane-depth/order, full-body panel width and forward/inverse checks across three sizes and three thicknesses.");
            }
            finally{RenderTexture.active=previous;Object.DestroyImmediate(profile);Object.DestroyImmediate(probe);Object.DestroyImmediate(input);Object.DestroyImmediate(read);Object.DestroyImmediate(rt);}
        }

        static void CheckCubeAppearance(StringBuilder report)
        {
            var directions=new[]{new Vector2(.5f,.5f),new Vector2(.75f,.5f),new Vector2(1,.5f),
                new Vector2(.25f,.5f),new Vector2(.5f,1),new Vector2(.5f,0),new Vector2(0,.5f)};
            int width=directions.Length;
            var probe=new Material(Shader.Find("Hidden/ShapeChange/Validation"));
            var atlas=new Texture2D(4,3,TextureFormat.RGBAFloat,false,true){wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Point};
            var input=new Texture2D(width,1,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point};
            var read=new Texture2D(width,1,TextureFormat.RGBAFloat,false,true);
            var rt=new RenderTexture(width,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var previous=RenderTexture.active;int count=0;
            try
            {
                atlas.SetPixels(Enumerable.Range(1,12).Select(tile=>new Color(tile/16f,0,0,1)).ToArray());atlas.Apply();
                input.SetPixels(directions.Select(uv=>new Color(uv.x,uv.y,0,1)).ToArray());input.Apply();
                probe.SetTexture("_SCCubeTex",atlas);probe.SetFloat("_SCMode",0);
                foreach(string key in new[]{"_ProbeCubeAppearance","_SCProxy","_SCCubeReady","_SCUpright","_SCSmoothEnvelope","_SCPreserveSurface"})probe.SetFloat(key,1);
                void Check(params int[] tiles)
                {
                    Graphics.Blit(input,rt,probe);RenderTexture.active=rt;
                    read.ReadPixels(new Rect(0,0,width,1),0,0);read.Apply();
                    var colors=read.GetPixels();
                    for(int i=0;i<width;i++)
                    {
                        if(Mathf.Abs(colors[i].r-(tiles[i]+1)/16f)>0.001f)
                            throw new Exception("Cube projection/limb routing failed at view "+i+"; expected tile "+tiles[i]+", got "+colors[i].r);
                        count++;
                    }
                }
                for(int arms=0;arms<3;arms++)for(int legs=0;legs<3;legs++)
                {
                    probe.SetFloat("_SCArms",arms);probe.SetFloat("_SCLegs",legs);
                    Check(arms==1?6:0,arms==1?7:1,2,arms==1?8:3,4,legs==1?9:5,2);
                }
                probe.SetFloat("_SCArms",0);probe.SetFloat("_SCLegs",0);
                probe.SetFloat("_SCArmMorph",1);probe.SetFloat("_SCLegMorph",1);
                Check(6,7,2,8,4,9,2);
                probe.SetFloat("_SCArmMorph",0);probe.SetFloat("_SCLegMorph",0);
                probe.SetFloat("_SCMode",1);Check(0,1,2,3,0,5,2);
                probe.SetFloat("_SCMode",2);Check(0,1,2,3,-1,5,2);
                probe.SetFloat("_SCMode",3);Check(0,1,2,3,-1,5,2);
                probe.SetFloat("_SCMode",4);Check(0,1,2,3,4,5,2);
                probe.SetFloat("_SCMode",5);Check(0,1,2,3,-1,5,2);
                probe.SetFloat("_SCMode",8);Check(0,-1,2,-1,-1,-1,2);
                probe.SetFloat("_SCMode",9);
                Check(-1,-1,-1,-1,-1,-1,-1);
                probe.SetFloat("_SCMode",0);probe.SetFloat("_SCCubeReady",0);
                Check(-1,-1,-1,-1,-1,-1,-1);
                report.AppendLine("PASS: "+count+" cube face, separate limb states and legacy appearance routing checks.");

                var cylinderUVs=new[]{new Vector2(.5f,.25f),new Vector2(.75f,.25f),new Vector2(.25f,.25f),
                    new Vector2(.5f,.75f),new Vector2(.75f,.75f),new Vector2(.99f,.75f),new Vector2(.01f,.75f)};
                input.SetPixels(cylinderUVs.Select(uv=>
                {
                    float angle=(uv.x-.5f)*2*Mathf.PI;
                    var rest=new Vector3(Mathf.Sin(angle),-Mathf.Cos(angle),uv.y*2-1).normalized;
                    return new Color(Mathf.Atan2(rest.x,rest.z)/(2*Mathf.PI)+.5f,rest.y*.5f+.5f,0,1);
                }).ToArray());input.Apply();
                probe.SetFloat("_ProbeCubeAppearance",0);probe.SetFloat("_ProbeCylinderSideUV",1);probe.SetFloat("_SCMode",6);
                probe.SetFloat("_SCBodyBase",.2f);probe.SetVector("_SCMeasure",new Vector4(.8f,.2f,.15f,.08f));
                Graphics.Blit(input,rt,probe);RenderTexture.active=rt;
                read.ReadPixels(new Rect(0,0,width,1),0,0);read.Apply();
                var cylinderValues=read.GetPixels();
                for(int i=0;i<width;i++)
                    if(Mathf.Abs(cylinderValues[i].r-cylinderUVs[i].x)>.001f ||
                       Mathf.Abs(cylinderValues[i].g-Mathf.Lerp(.2f,.8f,cylinderUVs[i].y))>.001f)
                        throw new Exception("Mode06 cylinder azimuth/height mapping failed at sample "+i+": "+cylinderValues[i]);
                report.AppendLine("PASS: "+width+" Mode06 cylinder side UV checks with shared heights and both sides of the rear seam.");
            }
            finally
            {
                RenderTexture.active=previous;rt.Release();Object.DestroyImmediate(rt);
                Object.DestroyImmediate(probe);Object.DestroyImmediate(atlas);Object.DestroyImmediate(input);Object.DestroyImmediate(read);
            }
        }

        static void CheckHeadProjection(StringBuilder report)
        {
            var probe=new Material(Shader.Find("Hidden/ShapeChange/Validation"));
            var input=new Texture2D(8,1,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point};
            var read=new Texture2D(8,1,TextureFormat.RGBAFloat,false,true);
            var rt=new RenderTexture(8,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var previous=RenderTexture.active;bool async=ShaderUtil.allowAsyncCompilation;int failures=0,checks=0;
            System.Action<bool,string> check=(ok,message)=>{checks++;if(!ok){failures++;report.AppendLine("FAIL "+message);}};
            System.Func<Color,Vector3> xyz=c=>new Vector3(c.r,c.g,c.b);
            System.Func<Color[]> sample=()=>{Graphics.Blit(input,rt,probe);RenderTexture.active=rt;read.ReadPixels(new Rect(0,0,8,1),0,0);read.Apply();return read.GetPixels();};
            try{
             ShaderUtil.allowAsyncCompilation=false;
             probe.SetVector("_SCFrame",new Vector4(0,0,0,1));probe.SetVector("_SCMeasure",new Vector4(.8f,.2f,.15f,.08f));
             probe.SetVector("_SCHead",new Vector4(0,.88f,.075f,0));probe.SetVector("_SCFaceSize",new Vector4(.15f,.2f,.78f,.98f));probe.SetVector("_SCBoxAspect",Vector4.one);
             foreach(string key in new[]{"_SCEnabled","_SCReady","_SCPreserveSurface","_SCSmoothEnvelope","_SCUpright","_SCNormalBlend","_SCSize"})probe.SetFloat(key,1);
             probe.SetFloat("_SCThickness",.02f);probe.SetFloat("_SCHeadFront",.09f);probe.SetFloat("_SCLayer",.025f);probe.SetFloat("_SCFaceCoverage",.4f);probe.SetFloat("_SCArms",1);probe.SetFloat("_SCLegs",1);
             var p=new Vector3(.025f,.86f,.06f);float step=.0002f;
             var points=new[]{p,p+Vector3.right*step,p+Vector3.up*step,p+Vector3.forward*.01f,p+new Vector3(.003f,.002f,.004f),p-Vector3.right*.025f,p+Vector3.up*.02f,p-Vector3.forward*.02f};
             input.SetPixels(points.Select(v=>new Color(v.x,v.y,v.z,1)).ToArray());input.Apply();
             foreach(float layer in new[]{.002f,.025f,.15f})foreach(int mode in new[]{0,1,3,4,6,7,8})foreach(float amount in new[]{0f,.25f,.5f,.75f,1f}){
              probe.SetFloat("_SCLayer",layer);
              probe.SetFloat("_SCMode",mode);probe.SetFloat("_SCAmount",amount);probe.SetFloat("_ProbeNormal",0);probe.SetFloat("_ProbeSkinned",0);
              var values=sample();float scale=amount==0?1:(values[1].r-values[0].r)/step;
              check(values.All(v=>Finite(v.r)&&Finite(v.g)&&Finite(v.b)),"finite "+mode+"/"+amount);
              if(mode!=6 || amount==1)check(Mathf.Abs((mode==6?-(values[2].b-values[0].b):values[2].g-values[0].g)/step-scale)<.004f,"shared face-plane scale "+mode+"/"+amount);
              var geometric=Vector3.Cross((xyz(values[1])-xyz(values[0]))/step,(xyz(values[2])-xyz(values[0]))/step).normalized;
              float depth=amount==1?(mode==6?values[3].g-values[0].g:values[3].b-values[0].b)/.01f:Vector3.Dot(xyz(values[3])-xyz(values[0]),geometric)/.01f;
              check(depth>0,"positive depth order "+mode+"/"+amount);
              if(amount==0)for(int i=0;i<8;i++)check((xyz(values[i])-points[i]).magnitude<.000001f,"original "+mode);
              if(amount==1)check(depth/scale>=(mode==8?.001f:.129f) && depth/scale<=(mode==8?.1f:.351f),"head depth must approach shape surface; mode="+mode+" ratio="+depth/scale);
              if(amount==1 && mode==6)check(values[0].g>1,"Cylinder face is below the end cap.");
              if(amount==1 && mode==8)check(Mathf.Abs(values[0].b)<.03f,"Coin head keeps its original depth.");
              if(amount==1 && mode==0)check(values[0].g>.42f && values[0].g<.45f && values[0].b>.34f && values[0].b<.37f,"Cube face is not at its recessed height and shallow front relief.");
              probe.SetFloat("_ProbeNormal",1);var normals=sample();check(Vector3.Dot(geometric,xyz(normals[0]))>.999f,"normal follows projected geometry "+mode+"/"+amount);
              probe.SetFloat("_ProbeNormal",0);probe.SetFloat("_ProbeSkinned",1);probe.SetMatrix("_ProbePose",Matrix4x4.Translate(points[4]-p));
              var posed=sample();check((xyz(posed[0])-xyz(values[4])).magnitude<.00001f,"posed face follows same map "+mode+"/"+amount);
             }
             probe.SetFloat("_SCMode",7);probe.SetFloat("_SCAmount",1);probe.SetFloat("_SCLayer",.025f);probe.SetFloat("_ProbeSkinned",0);probe.SetFloat("_ProbeNormal",0);probe.SetFloat("_SCProxy",0);
             input.SetPixels(Enumerable.Repeat(new Color(.025f,.86f,.03f,1),8).ToArray());input.Apply();var eye=sample()[0];
             float qx=eye.r/.28f;
             input.SetPixels(Enumerable.Repeat(new Color(qx*.15f,eye.g,Mathf.Sqrt(1-qx*qx)*.08f,0),8).ToArray());input.Apply();probe.SetFloat("_SCProxy",1);
             var shell=sample()[0];check(shell.b<eye.b-.002f,"Side cylinder proxy covers the retained face.");

              probe.SetFloat("_SCMode",6);probe.SetFloat("_SCUseProxy",1);
              var capLayers=new[]{new Color(.025f,.86f,-.07f,1),new Color(.013f,.78f,-.02f,1)};
              probe.SetFloat("_SCProxy",0);
              input.SetPixels(Enumerable.Repeat(capLayers,4).SelectMany(c=>c).ToArray());input.Apply();
              var projected=sample();
              for(int i=0;i<capLayers.Length;i++)
              {
                  float capX=projected[i].r/.28f,capZ=projected[i].b/.28f;
                  float capY=Mathf.Pow(1-Mathf.Pow(capX*capX+capZ*capZ,16),1f/32);
                  var direction=new Vector3(capX,capY,capZ).normalized;
                  input.SetPixels(Enumerable.Repeat(new Color(direction.x*.15f,.5f-direction.z*.5f,direction.y*.08f,0),8).ToArray());input.Apply();
                  probe.SetFloat("_SCProxy",1);
                  float cap=sample()[0].g;
                  check(cap<projected[i].g-.002f,"Top cylinder proxy covers the retained face at layer "+i+": "+cap+" / "+projected[i].g);
              }

             probe.SetFloat("_SCMode",0);probe.SetFloat("_SCProxy",0);probe.SetFloat("_SCUseProxy",1);probe.SetFloat("_ProbeFade",1);
             var cubeSamples=new[]{new Color(.03f,.72f,.08f,0),new Color(.025f,.86f,.03f,1),new Color(.03f,.77f,.08f,0),new Color(.28f,.65f,.02f,2)};
             input.SetPixels(cubeSamples.Concat(cubeSamples).ToArray());input.Apply();var cube=sample();
             check(cube[0].a>.99f && cube[1].a<.001f && cube[2].a<.001f,"Cube proxy replaces the stretched torso and preserves the face and collar.");
             probe.SetFloat("_SCArms",0);check(sample()[3].a<.001f,"Cube proxy leaves explicitly retained arms visible.");
             probe.SetFloat("_SCUseProxy",0);check(sample().All(v=>v.a<.001f),"Cube without proxy keeps its original coverage.");
             probe.SetFloat("_SCArms",1);probe.SetFloat("_SCAmount",.75f);var unassisted=sample()[3];
             probe.SetFloat("_SCUseProxy",1);var tucked=sample()[3];
             check(Mathf.Abs(tucked.r)<Mathf.Abs(unassisted.r)*.55f && tucked.a>.5f,"Stored sleeve should retract before the cube covers it.");
             probe.SetFloat("_SCAmount",0);
             check(sample().All(v=>v.a<.001f),"Cube at rest keeps its original coverage.");

             // Compare the proxy and face on the same viewing ray through the rounded cube.
             probe.SetFloat("_SCAmount",1);probe.SetFloat("_SCProxy",1);
             float x=cube[1].r/.35f,y=cube[1].g/.35f-1;
             float z=Mathf.Pow(1-Mathf.Pow(Mathf.Abs(x),32)-Mathf.Pow(Mathf.Abs(y),32),1f/32);
             var ray=new Vector3(x,y,z).normalized;
             input.SetPixels(Enumerable.Repeat(new Color(ray.x*.15f,.5f+ray.y*.5f,ray.z*.08f,0),8).ToArray());input.Apply();
             float cubeDepth=sample()[0].b;
             check(cubeDepth>.32f && cubeDepth<cube[1].b-.002f,"Cube face socket should be shallow and clear the retained face.");
            }finally{RenderTexture.active=previous;ShaderUtil.allowAsyncCompilation=async;UnityEngine.Object.DestroyImmediate(probe);UnityEngine.Object.DestroyImmediate(input);UnityEngine.Object.DestroyImmediate(read);rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
            if(failures>0)throw new Exception("Head projection regression: "+failures+" failures.");
            report.AppendLine("PASS: "+checks+" synthetic projected-head checks: identity, face-plane scale, positive depth order, surface normals and posed displacement; no full-mesh collision claim.");
        }

        static void CheckSurfaceAreas(GameObject root,StringBuilder report,string outputFolder)
        {
            var material=new Material(Shader.Find("Hidden/ShapeChange/Validation"));
            var previous=RenderTexture.active;
            try
            {
                var sample=ShapeChangeSetup.Materials(root).First(m=>m.GetFloat("_SCProxy")<0.5f);
                foreach(string key in new[]{"_SCFrame","_SCMeasure","_SCHead"})material.SetVector(key,sample.GetVector(key));
                foreach(string key in new[]{"_SCBodyBase","_SCFaceStart","_SCSize","_SCThickness","_SCLayer","_SCSurfaceFollow","_SCFaceCoverage"}) material.SetFloat(key,sample.GetFloat(key));
                CopyPosture(sample,material);
                material.SetFloat("_SCEnabled",1);material.SetFloat("_SCReady",1);material.SetFloat("_SCAmount",1);
                material.SetFloat("_SCArms",1);material.SetFloat("_SCLegs",1);material.SetFloat("_ProbeActual",1);
                float height=sample.GetVector("_SCFrame").w;
                var csv=new StringBuilder("mode,preserve,smooth_envelope,triangles,protected_triangles,protected_max_area_error_percent,face_triangles,face_max_area_or_length_error_percent,face_contract_residual_mm,area_weighted_abs_change_percent\n");
                for(int smooth=0;smooth<2;smooth++)for(int mode=0;mode<13;mode++)for(int preserve=0;preserve<2;preserve++)
                {
                    material.SetFloat("_SCSmoothEnvelope",smooth);
                    material.SetFloat("_SCMode",mode);material.SetFloat("_SCPreserveSurface",preserve);
                    bool projected=smooth==1 && sample.GetFloat("_SCUpright")>0.5f && (mode==0 || mode==1 || mode==3 || mode==4 || mode==6 || mode==7 || mode==8);
                    int triangles=0,protectedCount=0,faceCount=0;double areaSum=0,errorSum=0;float maxError=0,faceError=0,faceResidual=0;
                    foreach(var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        if(!ShapeChangeSetup.VisibleForMeasurement(renderer,root.transform) || !renderer.sharedMaterials.Any(m=>m && m.HasProperty("_SCReady") && m.GetFloat("_SCReady")>0 && m.GetFloat("_SCProxy")<0.5f && m.GetFloat("_SCAccessory")<0.5f && m.GetFloat("_SCAppendage")<0.5f))continue;
                        var mesh=renderer.sharedMesh;
                        var a=new System.Collections.Generic.List<Vector2>();var b=new System.Collections.Generic.List<Vector2>();var c=new System.Collections.Generic.List<Vector2>();
                        mesh.GetUVs(4,a);mesh.GetUVs(5,b);mesh.GetUVs(6,c);
                        int width=256,rows=(a.Count+width-1)/width;
                        var points=new Color[width*rows];var masks=new Color[points.Length];
                        for(int i=0;i<a.Count;i++){points[i]=new Color(a[i].x,a[i].y,b[i].x,1);masks[i]=new Color(b[i].y,c[i].x,c[i].y,1);}
                        var input=new Texture2D(width,rows,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point};
                        var weights=new Texture2D(width,rows,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point};
                        var output=new Texture2D(width,rows,TextureFormat.RGBAFloat,false,true);
                        var rt=new RenderTexture(width,rows,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
                        try
                        {
                            input.SetPixels(points);input.Apply();weights.SetPixels(masks);weights.Apply();material.SetTexture("_ProbeMasks",weights);
                            Graphics.Blit(input,rt,material);RenderTexture.active=rt;output.ReadPixels(new Rect(0,0,width,rows),0,0);output.Apply();
                            var result=output.GetPixels();var indices=mesh.triangles;
                            if(mode>=9)
                            {
                                var scale=FlatScale(sample,mode);var rotation=Quaternion.Euler(mode==9?-90:0,0,0);
                                float soft=Mathf.Max(sample.GetVector("_SCMeasure").w,0.025f)*Mathf.Clamp(sample.GetFloat("_SCLayer")*4,0.08f,0.4f);
                                float depth=Mathf.Max(sample.GetFloat("_SCThickness"),0.004f)*Mathf.Max(sample.GetFloat("_SCSize"),0.05f)*0.5f;
                                for(int i=1;i<a.Count;i++)
                                {
                                    var delta=Vector3.Scale(RGB(points[i])-RGB(points[0]),scale);
                                    delta.z=depth*(points[i].b/(Mathf.Abs(points[i].b)+soft)-points[0].b/(Mathf.Abs(points[0].b)+soft));
                                    var expected=rotation*delta*height;
                                    if((RGB(result[i])-RGB(result[0])-expected).magnitude>height*0.000003f)throw new Exception("Actual flat mesh correspondence changed: "+renderer.name);
                                }
                            }
                            for(int j=0;j<indices.Length;j+=3)
                            {
                                int i0=indices[j],i1=indices[j+1],i2=indices[j+2];
                                Vector3 p0=RGB(points[i0]),p1=RGB(points[i1]),p2=RGB(points[i2]);
                                float before=Vector3.Cross(p1-p0,p2-p0).magnitude;
                                if(before<1e-9f)continue;
                                float after=Vector3.Cross(RGB(result[i1])-RGB(result[i0]),RGB(result[i2])-RGB(result[i0])).magnitude/(height*height);
                                float error=Mathf.Abs(after/before-1);
                                triangles++;areaSum+=before;errorSum+=before*error;
                                if(result[i0].a>0 && result[i1].a>0 && result[i2].a>0 && p0.z*p1.z>0 && p0.z*p2.z>0)
                                {protectedCount++;float scale=result[i0].a;maxError=Mathf.Max(maxError,Mathf.Abs(after/before/(scale*scale)-1));}
                                if(result[i0].a<0 && result[i1].a<0 && result[i2].a<0)
                                {
                                    faceCount++;float scale=-result[i0].a;
                                    faceError=Mathf.Max(faceError,Mathf.Abs(after/before/(scale*scale)-1));
                                    foreach(var pair in new[]{(i0,i1),(i1,i2),(i2,i0)})
                                    {
                                        float length=Vector3.Distance(RGB(points[pair.Item1]),RGB(points[pair.Item2]));
                                        if(length>0.00001f)faceError=Mathf.Max(faceError,Mathf.Abs(Vector3.Distance(RGB(result[pair.Item1]),RGB(result[pair.Item2]))/(height*length*scale)-1));
                                        var expected=(RGB(points[pair.Item2])-RGB(points[pair.Item1]))*(height*scale);
                                        if(mode==6 || mode==9)expected=new Vector3(expected.x,expected.z,-expected.y);
                                        var residual=RGB(result[pair.Item2])-RGB(result[pair.Item1])-expected;
                                        if(projected){if(mode==6)residual.y=0;else residual.z=0;}
                                        faceResidual=Mathf.Max(faceResidual,residual.magnitude);
                                    }
                                }
                            }
                        }
                        finally{RenderTexture.active=previous;Object.DestroyImmediate(input);Object.DestroyImmediate(weights);Object.DestroyImmediate(output);Object.DestroyImmediate(rt);}
                    }
                    csv.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,"{0},{1},{2},{3},{4},{5:F5},{6},{7:F5},{8:F6},{9:F3}",mode,preserve,smooth,triangles,protectedCount,maxError*100,faceCount,faceError*100,faceResidual*1000,errorSum/Math.Max(areaSum,1e-12)*100));
                    if(preserve==1 && maxError>0.005f)throw new Exception("Protected panel area changed: mode="+mode+" relativeError="+maxError);
                    // Tiny eyelash/sliver triangles amplify float rounding in an area ratio.
                    // Test the actual edge vectors against a uniform transform as well.
                    if(preserve==1 && faceResidual>height*0.000002f)throw new Exception("Face correspondence changed: mode="+mode+" projected="+projected+" residual="+faceResidual);
                    if(mode<9 && preserve==1 && (smooth==0 && protectedCount==0 || faceCount==0))throw new Exception("No protected torso/face triangles were tested.");
                }
                File.WriteAllText(outputFolder+"/surface-area.csv",csv.ToString());
                report.AppendLine("PASS: actual mesh protected panels retain uniform scale; faces retain uniform face-plane correspondence in projected modes 0/1/3/4/6/7/8 and uniform 3D correspondence elsewhere. face_contract_residual_mm measures XZ for mode 6, XY for other projected faces, 3D otherwise; 3D area/length changes remain diagnostic. Sheet modes 9-12 retain their shared positive plane projection.");
            }
            finally{RenderTexture.active=previous;Object.DestroyImmediate(material);}
        }

        static void CheckAccessories(GameObject root,StringBuilder report)
        {
            var accessories=root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.sharedMaterials.Any(m=>m && m.HasProperty("_SCAccessory") && m.GetFloat("_SCAccessory")>0.5f)).ToArray();
            if(accessories.Length==0){report.AppendLine("SKIP: no shape-preserving attachments prepared.");return;}
            var probe=new Material(Shader.Find("Hidden/ShapeChange/Validation"));
            var input=new Texture2D(5,1,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point};
            var output=new Texture2D(5,1,TextureFormat.RGBAFloat,false,true);
            var rt=new RenderTexture(5,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var previous=RenderTexture.active;int checks=0;
            try
            {
                foreach(var renderer in accessories)
                {
                    var source=SourceAvatar(root);
                    string path=AnimationUtility.CalculateTransformPath(renderer.transform,root.transform);
                    var originalRenderer=source.transform.Find(path)?.GetComponent<Renderer>();
                    if(!originalRenderer)throw new Exception("Original attachment renderer missing: "+path);
                    var original=originalRenderer as SkinnedMeshRenderer;
                    if(original)
                    {
                        using(var a=original.sharedMesh.GetAllBoneWeights())using(var b=renderer.sharedMesh.GetAllBoneWeights())
                        {
                            if(a.Length!=b.Length)throw new Exception("Bone weights changed: "+renderer.name);
                            for(int i=0;i<a.Length;i++)if(a[i].boneIndex!=b[i].boneIndex || a[i].weight!=b[i].weight)throw new Exception("Bone weights changed: "+renderer.name);
                        }
                        if(original.bones.Length!=renderer.bones.Length)throw new Exception("Bone references changed: "+renderer.name);
                        for(int i=0;i<original.bones.Length;i++)if(AnimationUtility.CalculateTransformPath(original.bones[i],source.transform)!=AnimationUtility.CalculateTransformPath(renderer.bones[i],root.transform))throw new Exception("Bone binding changed: "+renderer.name);
                        if(original.sharedMesh.blendShapeCount!=renderer.sharedMesh.blendShapeCount)throw new Exception("Blend shapes lost: "+renderer.name);
                    }
                    var mat=renderer.sharedMaterials.First(m=>m && m.GetFloat("_SCAccessory")>0.5f);
                    if(mat.GetFloat("_SCAttachmentDataReady")<0.5f || !mat.GetTexture("_SCVertexData"))throw new Exception("Update attachment rest frames: "+renderer.name);
                    var uv=Enumerable.Range(4,4).Select(ch=>{var data=new System.Collections.Generic.List<Vector2>();renderer.sharedMesh.GetUVs(ch,data);return data;}).ToArray();
                    if(uv.Any(values=>values.Count!=renderer.sharedMesh.vertexCount))throw new Exception("Attachment coordinates missing: "+renderer.name);
                    var anchors=Enumerable.Range(0,renderer.sharedMesh.vertexCount).GroupBy(i=>(uv[0][i],uv[1][i],uv[2][i],uv[3][i])).Select(g=>g.First()).ToArray();
                    if(anchors.Length>7)throw new Exception("Attachment has per-vertex deformation instead of anatomical pivots: "+renderer.name+" ("+anchors.Length+" frames); run Upgrade Posture and Scale to refresh baked mesh data.");
                    foreach(int anchorIndex in anchors)
                    {
                    Vector3 anchor=new Vector3(uv[0][anchorIndex].x,uv[0][anchorIndex].y,uv[1][anchorIndex].x);
                    var points=new[]{anchor,anchor+new Vector3(0.012f,0,0),anchor+new Vector3(0,0.018f,0),anchor+new Vector3(0,0,0.009f),anchor+new Vector3(-0.009f,-0.013f,0.004f)};
                    input.SetPixels(points.Select(p=>new Color(p.x,p.y,p.z,1)).ToArray());input.Apply();
                    foreach(string key in new[]{"_SCFrame","_SCMeasure","_SCHead"})probe.SetVector(key,mat.GetVector(key));
                    foreach(string key in new[]{"_SCSize","_SCThickness","_SCBodyBase","_SCFaceStart","_SCFaceCoverage","_SCSurfaceFollow","_SCLayer","_SCPreserveSurface"})probe.SetFloat(key,mat.GetFloat(key));
                    CopyPosture(mat,probe);probe.SetFloat("_SCAccessoryScale",1);probe.SetFloat("_ProbeSkinned",1);
                    probe.SetFloat("_SCUseProxy",0);
                    probe.SetFloat("_SCReady",1);probe.SetFloat("_SCEnabled",1);probe.SetFloat("_SCAccessory",1);probe.SetFloat("_SCNormalBlend",1);
                    var data=mat.GetTexture("_SCVertexData") as Texture2D;
                    if(!data || data.height!=(renderer.sharedMesh.vertexCount+data.width-1)/data.width*3)throw new Exception("Invalid attachment frame texture: "+renderer.name);
                    probe.SetTexture("_SCVertexData",data);probe.SetFloat("_SCAttachmentDataReady",1);
                    probe.SetFloat("_SCMode",3);probe.SetFloat("_SCAmount",0.5f);
                    probe.SetMatrix("_ProbePose",Matrix4x4.TRS(new Vector3(0.04f,-0.02f,0.03f),Quaternion.Euler(13,27,-9),Vector3.one));
                    foreach(int vertexIndex in new[]{0,Mathf.Min(data.width-1,renderer.sharedMesh.vertexCount-1),Mathf.Min(data.width,renderer.sharedMesh.vertexCount-1),renderer.sharedMesh.vertexCount-1}.Distinct())
                    {
                        int x=vertexIndex%data.width,y=vertexIndex/data.width*3;
                        var rest=data.GetPixel(x,y);var restNormal=data.GetPixel(x,y+1);var restTangent=data.GetPixel(x,y+2);
                        input.SetPixels(Enumerable.Repeat(new Color(rest.r,rest.g,rest.b,1),5).ToArray());input.Apply();
                        probe.SetVector("_ProbeAnchor",new Vector3(uv[0][vertexIndex].x,uv[0][vertexIndex].y,uv[1][vertexIndex].x));
                        probe.SetVector("_ProbeAnchorMasks",new Vector3(uv[1][vertexIndex].y,uv[2][vertexIndex].x,uv[2][vertexIndex].y));
                        probe.SetVector("_ProbeAnchorNormal",uv[3][vertexIndex]);
                        probe.SetVector("_ProbeRestNormal",restNormal);probe.SetVector("_ProbeRestTangent",restTangent);probe.SetFloat("_ProbeVertexIndex",vertexIndex);
                        probe.SetFloat("_ProbePrepared",1);Graphics.Blit(input,rt,probe);RenderTexture.active=rt;output.ReadPixels(new Rect(0,0,5,1),0,0);output.Apply();var expected=RGB(output.GetPixel(0,0));
                        probe.SetFloat("_ProbePrepared",2);Graphics.Blit(input,rt,probe);RenderTexture.active=rt;output.ReadPixels(new Rect(0,0,5,1),0,0);output.Apply();
                        if((RGB(output.GetPixel(0,0))-expected).magnitude>0.000003f)throw new Exception("GPU attachment frame lookup mismatch: "+renderer.name+" vertex="+vertexIndex);
                    }
                    probe.SetFloat("_ProbePrepared",0);input.SetPixels(points.Select(p=>new Color(p.x,p.y,p.z,1)).ToArray());input.Apply();
                    probe.SetFloat("_SCNormalBlend",1);
                    var frame=mat.GetVector("_SCFrame");
                    probe.SetFloat("_SCReady",1);probe.SetFloat("_SCEnabled",1);probe.SetFloat("_SCAccessory",1);
                    probe.SetFloat("_SCArms",1);probe.SetFloat("_SCLegs",1);
                    probe.SetVector("_ProbeAnchor",anchor);probe.SetVector("_ProbeAnchorMasks",new Vector4(uv[1][anchorIndex].y,uv[2][anchorIndex].x,uv[2][anchorIndex].y,0));
                    probe.SetVector("_ProbeAnchorNormal",uv[3][anchorIndex]);
                    for(int auto=0;auto<2;auto++)for(int mode=0;mode<9;mode++)foreach(float amount in new[]{0f,0.25f,0.5f,1f})
                    {
                        probe.SetFloat("_SCMode",mode);probe.SetFloat("_SCAmount",amount);
                        probe.SetFloat("_SCAccessoryAutoScale",auto);
                        bool depthGuard=mode==8 && amount>0.5f && probe.GetFloat("_SCUpright")>0.5f
                            && probe.GetFloat("_SCSmoothEnvelope")>0.5f && probe.GetFloat("_SCPreserveSurface")>0.5f;
                        var poses=new[]{Matrix4x4.identity,Matrix4x4.TRS(new Vector3(0.04f,-0.02f,0.03f),Quaternion.Euler(13,27,-9),Vector3.one),Matrix4x4.TRS(new Vector3(-0.02f,0.03f,0.01f),Quaternion.Euler(0,180,0),Vector3.one)};
                        Matrix4x4 attachmentRotation=Matrix4x4.identity;
                        foreach(var pose in poses)
                        {
                            probe.SetMatrix("_ProbePose",pose);Graphics.Blit(input,rt,probe);RenderTexture.active=rt;
                            output.ReadPixels(new Rect(0,0,5,1),0,0);output.Apply();var actual=output.GetPixels();
                            {
                                if(!depthGuard)
                                {
                                    for(int axis=0;axis<3;axis++)attachmentRotation.SetColumn(axis,(RGB(actual[axis+1])-RGB(actual[0]))/((points[axis+1]-points[0]).magnitude*frame.w*actual[0].a));
                                    for(int axis=0;axis<3;axis++)if(Mathf.Abs(((Vector3)attachmentRotation.GetColumn(axis)).magnitude-1)>0.0002f)throw new Exception("Attachment axis scale mismatch: "+renderer.name);
                                    if(Mathf.Abs(attachmentRotation.determinant-1)>0.0003f)throw new Exception("Attachment contact frame sheared/reflected: "+renderer.name);
                                }
                                probe.SetFloat("_SCAccessory",0);probe.SetFloat("_ProbeContact",1);
                                Graphics.Blit(input,rt,probe);RenderTexture.active=rt;output.ReadPixels(new Rect(0,0,5,1),0,0);output.Apply();
                                if((RGB(output.GetPixel(0,0))-RGB(actual[0])).magnitude>frame.w*0.000003f)throw new Exception("Attachment contact drifted from owning surface: "+renderer.name+" mode="+mode+" amount="+amount);
                                probe.SetFloat("_ProbeNormal",1);Graphics.Blit(input,rt,probe);RenderTexture.active=rt;output.ReadPixels(new Rect(0,0,5,1),0,0);output.Apply();
                                var surfaceNormal=RGB(output.GetPixel(0,0));
                                probe.SetFloat("_SCAccessory",1);probe.SetFloat("_ProbeContact",0);
                                Graphics.Blit(input,rt,probe);RenderTexture.active=rt;output.ReadPixels(new Rect(0,0,5,1),0,0);output.Apply();
                                if(Vector3.Dot(surfaceNormal,RGB(output.GetPixel(0,0)))<0.995f)throw new Exception("Attachment tilts away from the intermediate contact surface: "+renderer.name+" mode="+mode+" amount="+amount+" surface="+surfaceNormal.ToString("F6")+" attachment="+RGB(output.GetPixel(0,0)).ToString("F6")+" anchorNormal="+uv[3][anchorIndex]);
                                probe.SetFloat("_ProbeNormal",0);
                            }
                            for(int i=1;i<points.Length;i++)
                            {
                                float scale=actual[0].a;
                                if(!Finite(scale) || scale<0.249f || scale>6.001f || auto==0 && Mathf.Abs(scale-1)>0.00001f)throw new Exception("Invalid attachment scale: "+renderer.name);
                                if(!depthGuard)
                                {
                                    Vector3 expected=attachmentRotation.MultiplyVector((points[i]-points[0])*frame.w)*scale;
                                    if((RGB(actual[i])-RGB(actual[0])-expected).magnitude>frame.w*0.000003f)throw new Exception("Attachment sheared: "+renderer.name+" mode="+mode);
                                }
                                if(amount==0 && (RGB(actual[i])-pose.MultiplyPoint3x4(points[i]*frame.w+(Vector3)frame)).magnitude>frame.w*0.000003f)throw new Exception("Attachment lost original skinning: "+renderer.name);
                                checks++;
                            }
                        }
                    }
                    }
                }
                report.AppendLine("PASS: "+accessories.Length+" attachments / "+checks+" GPU checks across 9 volume modes, auto-scale on/off, 4 blend amounts and three simulated skinned poses including a half-turn. Contact positions and normals match the owning deformation (normal dot >= 0.995). Similarity transforms verified outside the coin's inward depth guard. GPU rest textures, source skinned weights/bindings and blend-shape counts retained.");
            }
            finally{RenderTexture.active=previous;Object.DestroyImmediate(probe);Object.DestroyImmediate(input);Object.DestroyImmediate(output);Object.DestroyImmediate(rt);}
        }

        static void CheckCoinAttachments(StringBuilder report)
        {
            var probe=new Material(Shader.Find("Hidden/ShapeChange/Validation"));
            var input=new Texture2D(5,1,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point};
            var output=new Texture2D(5,1,TextureFormat.RGBAFloat,false,true);
            var target=new RenderTexture(5,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var previous=RenderTexture.active;
            Color[] Sample()
            {
                Graphics.Blit(input,target,probe);RenderTexture.active=target;
                output.ReadPixels(new Rect(0,0,5,1),0,0);output.Apply();return output.GetPixels();
            }
            try
            {
                foreach(string key in new[]{"_SCEnabled","_SCReady","_SCPreserveSurface","_SCSmoothEnvelope","_SCUpright","_SCNormalBlend","_SCSize","_SCAccessoryScale"})probe.SetFloat(key,1);
                probe.SetVector("_SCFrame",new Vector4(0,0,0,1));
                probe.SetVector("_SCMeasure",new Vector4(.8f,.2f,.15f,.08f));
                probe.SetVector("_SCHead",new Vector4(0,.88f,.075f,0));
                probe.SetFloat("_SCThickness",.02f);probe.SetFloat("_SCLayer",.025f);
                probe.SetFloat("_SCMode",8);probe.SetFloat("_SCUseProxy",1);
                probe.SetMatrix("_ProbePose",Matrix4x4.identity);
                foreach(float side in new[]{-1f,1f})
                {
                    var anchor=new Vector3(0,.5f,side*.08f);
                    var points=new[]{anchor,anchor+Vector3.forward*(side*.05f),anchor-Vector3.forward*(side*.08f),
                        anchor-Vector3.forward*(side*.12f),anchor-Vector3.forward*(side*.16f)};
                    input.SetPixels(points.Select(p=>new Color(p.x,p.y,p.z,0)).ToArray());input.Apply();
                    probe.SetVector("_ProbeAnchor",anchor);
                    probe.SetVector("_ProbeAnchorNormal",side<0?Vector2.one:Vector2.zero);
                    probe.SetFloat("_SCAccessory",1);probe.SetFloat("_SCAmount",0);
                    if(Sample().Where((c,i)=>(RGB(c)-points[i]).sqrMagnitude>1e-10f).Any())throw new Exception("Coin attachment changed at rest.");
                    foreach(float amount in new[]{.75f,1f})
                    {
                        probe.SetFloat("_SCAmount",amount);probe.SetFloat("_SCAccessory",1);
                        var decoration=Sample();
                        probe.SetFloat("_SCAccessory",0);
                        if((RGB(decoration[0])-RGB(Sample()[0])).sqrMagnitude>1e-10f)throw new Exception("Coin attachment moved away from its owner.");
                        if(side*decoration[4].b<=0 || side*decoration[2].b<=side*decoration[3].b || side*decoration[3].b<=side*decoration[4].b)
                            throw new Exception($"Coin attachment crossed its owning side or lost depth order at {amount:P0}: {side} / {decoration[4].b}.");
                        if(Mathf.Abs(side*(decoration[1].b-decoration[0].b)-.05f)>1e-5f)throw new Exception("Coin attachment lost outward relief.");
                    }
                }
                report.AppendLine("PASS: coin attachments stay on their owning side at 75/100% with ordered inward relief, unchanged outward relief and fixed contact.");
            }
            finally
            {
                RenderTexture.active=previous;Object.DestroyImmediate(probe);Object.DestroyImmediate(input);
                Object.DestroyImmediate(output);target.Release();Object.DestroyImmediate(target);
            }
        }

        static void CheckInflatedAttachments(StringBuilder report)
        {
            var probe=new Material(Shader.Find("Hidden/ShapeChange/Validation"));
            var input=new Texture2D(2,1,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point};
            var output=new Texture2D(2,1,TextureFormat.RGBAFloat,false,true);
            var target=new RenderTexture(2,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var previous=RenderTexture.active;
            Color[] Sample(Vector3 root, Vector3 tip)
            {
                input.SetPixels(new[]{new Color(root.x,root.y,root.z,0),new Color(tip.x,tip.y,tip.z,0)});input.Apply();
                Graphics.Blit(input,target,probe);RenderTexture.active=target;
                output.ReadPixels(new Rect(0,0,2,1),0,0);output.Apply();return output.GetPixels();
            }
            try
            {
                foreach(string key in new[]{"_SCEnabled","_SCReady","_SCPreserveSurface","_SCSmoothEnvelope","_SCUpright","_SCNormalBlend","_SCSize","_SCAccessoryScale"})probe.SetFloat(key,1);
                probe.SetVector("_SCFrame",new Vector4(0,0,0,1));
                probe.SetVector("_SCMeasure",new Vector4(.8f,.2f,.15f,.08f));
                probe.SetVector("_SCHead",new Vector4(0,.88f,.075f,0));
                probe.SetVector("_SCFaceSize",new Vector4(.15f,.2f,.78f,.98f));
                probe.SetFloat("_SCHeadFront",.09f);probe.SetFloat("_SCLayer",.025f);
                probe.SetFloat("_SCMode",3);probe.SetMatrix("_ProbePose",Matrix4x4.identity);
                var anchor=new Vector3(0,.5f,-.027f);
                var tip=anchor+Vector3.back*.07f;
                var shellInput=new Vector3(0,.5f,-.08f);
                probe.SetVector("_ProbeAnchor",anchor);probe.SetVector("_ProbeAnchorNormal",Vector2.one);
                foreach(float inflation in new[]{.65f,1.8f})foreach(float amount in new[]{0f,.5f,.75f,1f})
                {
                    probe.SetFloat("_SCInflation",inflation);probe.SetFloat("_SCAmount",amount);
                    probe.SetFloat("_SCUseProxy",0);probe.SetFloat("_SCProxy",0);probe.SetFloat("_SCAccessory",0);
                    var skin=Sample(anchor,tip)[0];
                    probe.SetFloat("_SCAccessory",1);
                    if((RGB(Sample(anchor,tip)[0])-RGB(skin)).sqrMagnitude>1e-10f)throw new Exception("Inflated anchor drifted when the proxy was disabled.");
                    probe.SetFloat("_SCUseProxy",1);
                    var accessory=Sample(anchor,tip);
                    if(amount==0 && ((RGB(accessory[0])-anchor).sqrMagnitude>1e-10f || (RGB(accessory[1])-tip).sqrMagnitude>1e-10f))
                        throw new Exception("Inflated attachment changed at rest.");
                    if(amount<=.5f && (RGB(accessory[0])-RGB(skin)).sqrMagnitude>1e-10f)throw new Exception("Inflated attachment left the live owner before shell takeover.");
                    if(amount<=.5f)continue;
                    probe.SetFloat("_SCAccessory",0);probe.SetFloat("_SCProxy",1);
                    var shell=Sample(shellInput,shellInput)[0];
                    if(accessory[0].b>shell.b+.002f || amount==1 && Mathf.Abs(accessory[0].b-shell.b)>.002f || accessory[1].b>shell.b-.025f)
                        throw new Exception($"Inflated rear attachment is buried or detached at {amount:P0}, inflation={inflation}: anchor={accessory[0].b}, tip={accessory[1].b}, shell={shell.b}.");
                    probe.SetFloat("_SCProxy",0);probe.SetFloat("_SCAccessory",1);probe.SetFloat("_ProbeNormal",1);
                    if(Sample(anchor,tip)[0].b>-.99f)throw new Exception("Inflated attachment normal did not follow the rear shell.");
                    probe.SetFloat("_ProbeNormal",0);
                }
                var lowAnchor=new Vector3(0,.25f,-.08f);
                var lowTip=lowAnchor+Vector3.up*.025f+Vector3.back*.06f;
                probe.SetVector("_ProbeAnchor",lowAnchor);
                var lowAttachment=Sample(lowAnchor,lowTip);
                var lowOffset=RGB(lowAttachment[1])-RGB(lowAttachment[0]);
                if(Mathf.Abs(lowOffset.y-.025f)>.0002f || lowOffset.z>-.059f)
                    throw new Exception("Inflated shell tilted the posed ornament: "+lowOffset.ToString("F6"));
                var frontAnchor=new Vector3(0,.70f,.08f);
                var frontShellInput=new Vector3(0,.70f,.08f*Mathf.Sqrt(1-.4f*.4f));
                probe.SetFloat("_SCInflation",1);probe.SetVector("_ProbeAnchor",frontAnchor);
                probe.SetVector("_ProbeAnchorNormal",Vector2.zero);
                var frontMount=Sample(frontAnchor,frontAnchor+Vector3.forward*.04f);
                probe.SetFloat("_SCAccessory",0);probe.SetFloat("_SCProxy",1);
                var frontShell=Sample(frontShellInput,frontShellInput)[0];
                if((RGB(frontMount[0])-RGB(frontShell)).sqrMagnitude>4e-6f || frontMount[1].b<frontShell.b+.025f)
                    throw new Exception($"Inflated front attachment missed the recessed shell: mount={RGB(frontMount[0])}, shell={RGB(frontShell)}.");
                report.AppendLine("PASS: inflated body attachment follows the live owner and the visible ellipsoid at two inflation values and four stages; rear relief, off-equator posed pitch and recessed front contact verified.");
            }
            finally
            {
                RenderTexture.active=previous;Object.DestroyImmediate(probe);Object.DestroyImmediate(input);
                Object.DestroyImmediate(output);target.Release();Object.DestroyImmediate(target);
            }
        }

        static Vector3 RGB(Color c)=>new Vector3(c.r,c.g,c.b);

        static void CopyPosture(Material source,Material target)
        {
            target.SetTexture("_SCPanelProfile",source.GetTexture("_SCPanelProfile"));
            target.SetVector("_SCFaceSize",source.GetVector("_SCFaceSize"));
            target.SetVector("_SCBoxAspect",source.GetVector("_SCBoxAspect"));target.SetFloat("_SCInflation",source.GetFloat("_SCInflation"));
            foreach(string key in new[]{"_SCArmL","_SCArmR","_SCLegL","_SCLegR"})target.SetVector(key,source.GetVector(key));
            foreach(string key in new[]{"_SCUpright","_SCSeated","_SCRigReady","_SCHeadFront","_SCPanelBottom","_SCSmoothEnvelope","_SCArmMorph","_SCLegMorph"})target.SetFloat(key,source.GetFloat(key));
        }

        static void CheckLimbMorph(GameObject root,StringBuilder report)
        {
            var probe=new Material(Shader.Find("Hidden/ShapeChange/Validation"));
            var template=ShapeChangeSetup.Materials(root).First(m=>m.GetFloat("_SCProxy")<0.5f && m.GetFloat("_SCAccessory")<0.5f);
            probe.CopyPropertiesFromMaterial(template);CopyPosture(template,probe);
            var points=new[]{new Color(-0.5f,0.76f,0,2),new Color(0.5f,0.76f,0,2),new Color(-0.06f,0.1f,0,3),new Color(0.06f,0.1f,0,3)};
            var input=new Texture2D(4,1,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point};input.SetPixels(points);input.Apply();
            var output=new Texture2D(4,1,TextureFormat.RGBAFloat,false,true);
            var rt=new RenderTexture(4,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var previous=RenderTexture.active;int checks=0;
            Func<Color[]> sample=()=>{Graphics.Blit(input,rt,probe);RenderTexture.active=rt;output.ReadPixels(new Rect(0,0,4,1),0,0);output.Apply();return output.GetPixels();};
            try
            {
                probe.SetFloat("_SCProxy",0);probe.SetFloat("_SCAccessory",0);probe.SetFloat("_SCAppendage",0);probe.SetFloat("_ProbeFade",1);probe.SetFloat("_SCUseProxy",1);
                float tolerance=template.GetVector("_SCFrame").w*0.00001f;
                for(int mode=0;mode<13;mode++)foreach(float amount in new[]{0f,0.5f,1f})
                {
                    probe.SetFloat("_SCMode",mode);probe.SetFloat("_SCAmount",amount);
                    probe.SetFloat("_SCArms",1);probe.SetFloat("_SCLegs",1);var folded=sample();
                    probe.SetFloat("_SCArms",0);probe.SetFloat("_SCLegs",0);
                    foreach(float morph in new[]{0f,0.25f,0.5f,0.75f,1f})
                    {
                        probe.SetFloat("_SCArmMorph",morph);probe.SetFloat("_SCLegMorph",morph);var values=sample();
                        for(int i=0;i<values.Length;i++)
                        {
                            var p=RGB(values[i]);
                            if(!Finite(p.x)||!Finite(p.y)||!Finite(p.z))throw new Exception("Non-finite limb morph: "+mode+" / "+morph);
                            if(morph==1 && ((p-RGB(folded[i])).magnitude>tolerance || Mathf.Abs(values[i].a-folded[i].a)>0.00001f))throw new Exception("Limb morph=1 differs from Fold: "+mode);
                            var frame=template.GetVector("_SCFrame");
                            if(amount==0 && (p-(RGB(points[i])*frame.w+(Vector3)frame)).magnitude>tolerance)throw new Exception("Limb morph changed original mesh.");
                            checks++;
                        }
                    }
                    // Changing the arm control must not change either leg. Leg
                    // controls can lift the whole body, so the reverse is not a
                    // valid world-space invariant for a standing avatar.
                    probe.SetFloat("_SCLegMorph",0);probe.SetFloat("_SCArmMorph",0);var armsOut=sample();
                    probe.SetFloat("_SCArmMorph",1);var armsIn=sample();
                    for(int i=2;i<4;i++)if((RGB(armsOut[i])-RGB(armsIn[i])).magnitude>tolerance)throw new Exception("Arm morph moves the legs.");
                }
                report.AppendLine("PASS: "+checks+" GPU limb-morph samples across 13 shapes, 5 morph values and 3 deformation amounts; morph=1 matches Fold in position/coverage, amount=0 is unchanged, arm control leaves legs unchanged.");
                probe.SetFloat("_SCAmount",1);probe.SetFloat("_SCUseProxy",0);probe.SetFloat("_SCArms",1);probe.SetFloat("_SCLegs",1);
                probe.SetMatrix("_ProbePose",Matrix4x4.identity);probe.SetVector("_ProbeAnchor",new Vector4(0,0.5f,0,0));
                probe.SetVector("_ProbeAnchorMasks",new Vector4(0,1,0,0));
                foreach(float accessory in new[]{0f,1f})
                {
                    probe.SetFloat("_SCAccessory",accessory);
                    if(sample().Any(v=>v.a>0.00001f))throw new Exception("Surface fades when the closed proxy is disabled.");
                }
                probe.SetFloat("_SCAccessory",0);probe.SetFloat("_SCUseProxy",1);
                report.AppendLine("PASS: original limb surfaces and attachments do not fade when the closed proxy is disabled.");
                probe.SetFloat("_SCAmount",1);probe.SetFloat("_SCUpright",1);probe.SetFloat("_SCPreserveSurface",1);
                probe.SetFloat("_SCArms",1);probe.SetFloat("_SCLegs",1);
                var face=template.GetVector("_SCFaceSize");var head=template.GetVector("_SCHead");var measure=template.GetVector("_SCMeasure");
                float front=head.w+template.GetFloat("_SCHeadFront")*0.6f;
                if(face.w<=face.z+0.03f)throw new Exception("Head span is not calibrated; run Upgrade Posture and Scale.");
                var headSamples=new[]{new Color(head.x,face.w,front,1),new Color(head.x,face.z,front,1),new Color(head.x,measure.x-0.015f,front,0),new Color(head.x+0.01f,measure.x-0.015f,front,0)};
                for(int mode=0;mode<6;mode++)
                {
                    input.SetPixels(headSamples);input.Apply();probe.SetFloat("_SCProxy",0);
                    probe.SetFloat("_SCMode",mode);var values=sample();
                    float shapeHeight=new[]{0.7f,0.22f,0.56f,1f,0.7f,0.5f}[mode]*template.GetFloat("_SCSize")*template.GetVector("_SCFrame").w;
                    if(mode==1)shapeHeight*=Mathf.Max(template.GetVector("_SCBoxAspect").y,0.1f);
                    float expectedHeadHeight=mode==3?(face.w-face.z)*template.GetFloat("_SCSize")*template.GetVector("_SCFrame").w:shapeHeight*0.4f;
                    float scale=expectedHeadHeight/(face.w-face.z);
                    bool projected=template.GetFloat("_SCSmoothEnvelope")>0.5f && (mode==0 || mode==1 || mode==3 || mode==4);
                    var span=RGB(values[0])-RGB(values[1]);var collar=RGB(values[2])-RGB(values[3]);
                    if(projected){span.z=0;collar.z=0;}
                    if(Mathf.Abs(span.magnitude-expectedHeadHeight)>tolerance)throw new Exception("Crown-to-chin scale changed: "+mode);
                    if(Mathf.Abs(collar.magnitude-scale*0.01f)>tolerance || values.Any(v=>v.a>0.0001f))throw new Exception("Retained chin/collar correspondence or coverage changed: "+mode);
                    if(mode==2 || mode==5)
                    {
                        input.SetPixels(Enumerable.Repeat(new Color(0,1,0,0),4).ToArray());input.Apply();probe.SetFloat("_SCProxy",1);
                        float bodyTop=sample()[0].g-template.GetVector("_SCFrame").y;
                        if(Mathf.Abs(bodyTop/shapeHeight-0.6f)>0.0001f)throw new Exception("Headed body cage rises above 60% of the reference height: "+mode);
                    }
                }
                report.AppendLine("PASS: projected crown-to-chin XY span = 40% in modes 0/1/4; headed modes 2/5 keep the 3D span and inflation retains original face-plane size. Central collar XY scale and coverage remain intact; headed cages end at 60%.");
            }
            finally{RenderTexture.active=previous;Object.DestroyImmediate(probe);Object.DestroyImmediate(input);Object.DestroyImmediate(output);Object.DestroyImmediate(rt);}
        }

        static void CheckPoseSizes(GameObject root,StringBuilder report)
        {
            var probe=new Material(Shader.Find("Hidden/ShapeChange/Validation"));
            var template=ShapeChangeSetup.Materials(root).First(m=>m.GetFloat("_SCProxy")<0.5f);
            probe.CopyPropertiesFromMaterial(template);CopyPosture(template,probe);probe.SetFloat("_ProbeShape",1);
            var rt=new RenderTexture(1,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var output=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);var previous=RenderTexture.active;int checks=0;
            try
            {
                foreach(float size in new[]{0.2f,1f,2f})foreach(float inflation in new[]{0.5f,2.5f})
                foreach(var aspect in new[]{Vector4.one,new Vector4(1.6f,0.75f,1.3f,0)})for(int mode=0;mode<=5;mode++)
                {
                    probe.SetFloat("_SCMode",mode);probe.SetFloat("_SCSize",size);probe.SetFloat("_SCInflation",inflation);probe.SetVector("_SCBoxAspect",aspect);
                    Graphics.Blit(Texture2D.blackTexture,rt,probe);RenderTexture.active=rt;output.ReadPixels(new Rect(0,0,1,1),0,0);output.Apply();
                    var measured=RGB(output.GetPixel(0,0));
                    if((measured-ShapeChangePose.HalfSize(probe)).magnitude>0.00001f)throw new Exception("CPU pose size differs from GPU body size: "+mode);
                    if(mode==1 && aspect==Vector4.one && (measured-new Vector3(0.24f,0.11f,0.43f)*size).magnitude>0.00001f)
                        throw new Exception("Default cuboid dimensions differ from the horizontal profile.");
                    checks++;
                }
                report.AppendLine("PASS: "+checks+" CPU pose / GPU body-size pairs match across six seated modes, three sizes, two inflation factors and two box aspects.");
            }
            finally{RenderTexture.active=previous;Object.DestroyImmediate(probe);Object.DestroyImmediate(rt);Object.DestroyImmediate(output);}
        }

        static void Render(GameObject root, StringBuilder report)
        {
            var mats=ShapeChangeSetup.Materials(root);
            float height=mats[0].GetVector("_SCFrame").w;
            var old=mats.Select(m=>new Vector4(m.GetFloat("_SCMode"),m.GetFloat("_SCAmount"),m.GetFloat("_SCArms"),m.GetFloat("_SCLegs"))).ToArray();
            var transforms=root.GetComponentsInChildren<Transform>(true);
            var layers=transforms.Select(t=>t.gameObject.layer).ToArray();
            var active=transforms.Select(t=>t.gameObject.activeSelf).ToArray();
            var cameraObject=new GameObject("ShapeChange Validation Camera",typeof(Camera));
            var camera=cameraObject.GetComponent<Camera>();
            camera.enabled=false; camera.cullingMask=1<<30;
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(0.12f,0.15f,0.2f,1);
            camera.orthographic=true; camera.orthographicSize=height*root.transform.lossyScale.y*0.77f; camera.nearClipPlane=0.001f; camera.farClipPlane=100;
            camera.transform.position=root.transform.TransformPoint(new Vector3(height*1.5f,height*1.3f,height*3));
            camera.transform.LookAt(root.transform.TransformPoint(new Vector3(0,height*0.55f,0)));
            const int width=360,heightPx=400;
            var rt=new RenderTexture(width,heightPx,24,RenderTextureFormat.ARGB32);
            var tex=new Texture2D(width,heightPx,TextureFormat.RGB24,false);
            var sheet=new Texture2D(width*4,heightPx*4,TextureFormat.RGB24,false);
            sheet.SetPixels(Enumerable.Repeat(camera.backgroundColor,width*4*heightPx*4).ToArray());
            var previous=RenderTexture.active;
            try
            {
                foreach(var t in transforms) t.gameObject.layer=30;
                camera.targetTexture=rt;
                for(int index=0;index<16;index++)
                {
                    int mode=index-1;
                    if(index<14) ShapeChangeSetup.SetShape(root,Mathf.Max(0,mode),index==0?0:1);
                    else if(index==14) ShapeChangeSetup.SetShape(root,4,0.5f,0,0);
                    else ShapeChangeSetup.SetShape(root,4,1,2,2);
                    camera.Render(); RenderTexture.active=rt;
                    tex.ReadPixels(new Rect(0,0,width,heightPx),0,0); tex.Apply();
                    var pixels=tex.GetPixels();
                    if(pixels.Count(c=>c.r>0.9f && c.b>0.9f && c.g<0.1f)>pixels.Length/10) throw new Exception("Pink shader output: "+index);
                    sheet.SetPixels(index%4*width,(3-index/4)*heightPx,width,heightPx,pixels);
                    File.WriteAllBytes(Output+"/"+index.ToString("D2")+".png",tex.EncodeToPNG());
                    report.AppendLine(index.ToString("D2")+": "+(index==0?"Original":index<14?ShapeChangeSetup.Modes[mode]:index==14?"Sphere 50% / keep limbs":"Sphere / hide limbs"));
                }
                sheet.Apply(); File.WriteAllBytes(Output+"/contact-sheet.png",sheet.EncodeToPNG());
                camera.transform.position=root.transform.TransformPoint(new Vector3(0,height*0.4f,height*3));
                camera.transform.LookAt(root.transform.TransformPoint(new Vector3(0,height*0.4f,0)));
                camera.orthographicSize=height*root.transform.lossyScale.y*0.52f;
                foreach(int shape in new[]{0,4})
                {
                    ShapeChangeSetup.SetShape(root,shape,1);
                    camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,width,heightPx),0,0);tex.Apply();
                    File.WriteAllBytes(Output+(shape==0?"/cube-front.png":"/sphere-front.png"),tex.EncodeToPNG());
                }
                foreach(var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if(r.sharedMaterials.Any(m=>m && m.HasProperty("_SCAccessory") && m.GetFloat("_SCAccessory")>0.5f))r.gameObject.SetActive(true);
                ShapeChangeSetup.SetShape(root,4,1,0,1);
                camera.orthographicSize=height*root.transform.lossyScale.y*0.8f;
                camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,width,heightPx),0,0);tex.Apply();
                File.WriteAllBytes(Output+"/accessories-keep.png",tex.EncodeToPNG());
                report.AppendLine("Rendered shape-preserving accessories with arms kept; normally inactive attachments enabled only for this capture.");
                for(int i=0;i<transforms.Length;i++)transforms[i].gameObject.SetActive(active[i]);
                RenderEarTailCases(root,camera,rt,tex,report);
                RenderPostureCases(root,camera,rt,tex,report);
                RenderMorphCases(root,camera,rt,tex,report);
                RenderReferenceCases(root,camera,rt,tex,report);
                RenderTopologyCases(root,camera,rt,tex,report);
                RenderAddedCases(root,camera,rt,tex,report);
            }
            finally
            {
                for(int i=0;i<mats.Length;i++) { mats[i].SetFloat("_SCMode",old[i].x);mats[i].SetFloat("_SCAmount",old[i].y);mats[i].SetFloat("_SCArms",old[i].z);mats[i].SetFloat("_SCLegs",old[i].w); }
                for(int i=0;i<transforms.Length;i++) {transforms[i].gameObject.layer=layers[i];transforms[i].gameObject.SetActive(active[i]);}
                RenderTexture.active=previous; camera.targetTexture=null;
                Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); Object.DestroyImmediate(sheet);
            }
        }

        static void RenderTopologyCases(GameObject root,Camera camera,RenderTexture rt,Texture2D image,StringBuilder report)
        {
            var mats=ShapeChangeSetup.Materials(root);
            var keys=new[]{"_SCUseProxy","_SCUpright","_SCSeated","_SCSmoothEnvelope","_SCPreserveSurface"};
            var old=mats.Select(m=>keys.Select(m.GetFloat).ToArray()).ToArray();
            float height=mats[0].GetVector("_SCFrame").w;
            var sheet=new Texture2D(image.width*5,image.height*4,TextureFormat.RGB24,false);
            try
            {
                foreach(var m in mats){m.SetFloat("_SCUseProxy",0);m.SetFloat("_SCUpright",1);m.SetFloat("_SCSeated",0);m.SetFloat("_SCSmoothEnvelope",1);m.SetFloat("_SCPreserveSurface",1);}
                camera.orthographicSize=height*root.transform.lossyScale.y*0.78f;
                camera.transform.position=root.transform.TransformPoint(new Vector3(height*0.65f,height*0.72f,height*3));
                camera.transform.LookAt(root.transform.TransformPoint(new Vector3(0,height*0.5f,0)));
                for(int row=0;row<4;row++)for(int column=0;column<5;column++)
                {
                    ShapeChangeSetup.SetShape(root,new[]{0,4,7,10}[row],column*0.25f,1,1);
                    camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,image.width,image.height),0,0);image.Apply();
                    sheet.SetPixels(column*image.width,(3-row)*image.height,image.width,image.height,image.GetPixels());
                }
                sheet.Apply();File.WriteAllBytes(Output+"/topology-sequence-comparison.png",sheet.EncodeToPNG());
                report.AppendLine("Topology-only diagnostic: original surfaces, no closed proxy or limb hiding; columns=amount 0/0.25/0.5/0.75/1; rows=cube/sphere/side-face cylinder/front-face plane. This is the current mapping baseline, not a fitted reconstruction of the cited illustrations.");
                // Reuse the sheet for front/side views of the two sheet orientations.
                for(int row=0;row<4;row++)for(int column=0;column<5;column++)
                {
                    ShapeChangeSetup.SetShape(root,row<2?10:9,column*0.25f,1,1);
                    bool side=row%2==1;
                    camera.transform.position=root.transform.TransformPoint(new Vector3(side?height*3:0,height*0.6f,side?0:height*3));
                    camera.transform.LookAt(root.transform.TransformPoint(new Vector3(0,height*0.45f,0)));
                    camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,image.width,image.height),0,0);image.Apply();
                    sheet.SetPixels(column*image.width,(3-row)*image.height,image.width,image.height,image.GetPixels());
                }
                sheet.Apply();File.WriteAllBytes(Output+"/flat-sequence-comparison.png",sheet.EncodeToPNG());
                report.AppendLine("Sheet sequence: columns=amount 0/0.25/0.5/0.75/1; rows=vertical front/side, horizontal front/side. Original skinned surfaces remain present with positive thickness.");
            }
            finally{for(int i=0;i<mats.Length;i++)for(int k=0;k<keys.Length;k++)mats[i].SetFloat(keys[k],old[i][k]);Object.DestroyImmediate(sheet);}
        }

        static void RenderReferenceCases(GameObject root,Camera camera,RenderTexture rt,Texture2D image,StringBuilder report)
        {
            var mats=ShapeChangeSetup.Materials(root);var old=mats.Select(m=>new Vector3(m.GetFloat("_SCSmoothEnvelope"),m.GetFloat("_SCUpright"),m.GetFloat("_SCSeated"))).ToArray();
            float height=mats[0].GetVector("_SCFrame").w;var sheet=new Texture2D(image.width*4,image.height*4,TextureFormat.RGB24,false);
            try
            {
                for(int row=0;row<4;row++)for(int column=0;column<4;column++)
                {
                    ShapeChangeSetup.SetShape(root,new[]{0,2,4,5}[column],row==3?0.5f:1);
                    foreach(var m in mats){m.SetFloat("_SCSmoothEnvelope",row==0?0:1);m.SetFloat("_SCUpright",1);m.SetFloat("_SCSeated",0);}
                    camera.orthographicSize=height*root.transform.lossyScale.y*0.57f;
                    camera.transform.position=root.transform.TransformPoint(new Vector3(row==2?height*3:0,height*0.4f,row==2?0:height*3));
                    camera.transform.LookAt(root.transform.TransformPoint(new Vector3(0,height*0.4f,0)));
                    camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,image.width,image.height),0,0);image.Apply();
                    sheet.SetPixels(column*image.width,(3-row)*image.height,image.width,image.height,image.GetPixels());
                }
                sheet.Apply();File.WriteAllBytes(Output+"/reference-comparison.png",sheet.EncodeToPNG());
                report.AppendLine("Reference comparison: columns=cube/headed cube/sphere/2.5-head sphere; rows=old panel preservation/smooth front/smooth side/smooth at 50%.");
            }
            finally{for(int i=0;i<mats.Length;i++){mats[i].SetFloat("_SCSmoothEnvelope",old[i].x);mats[i].SetFloat("_SCUpright",old[i].y);mats[i].SetFloat("_SCSeated",old[i].z);}Object.DestroyImmediate(sheet);}
        }

        static void RenderMorphCases(GameObject root,Camera camera,RenderTexture rt,Texture2D image,StringBuilder report)
        {
            var source=SourceAvatar(root);var mats=ShapeChangeSetup.Materials(root);
            var bones=ShapeChangePose.PoseBones.Select(root.GetComponent<Animator>().GetBoneTransform).ToArray();var rotations=bones.Select(b=>b.localRotation).ToArray();
            var old=mats.Select(m=>new Vector4(m.GetFloat("_SCArmMorph"),m.GetFloat("_SCLegMorph"),m.GetFloat("_SCSeated"),m.GetFloat("_SCUpright"))).ToArray();
            var skins=root.GetComponentsInChildren<SkinnedMeshRenderer>(true);var recalc=skins.Select(r=>r.forceMatrixRecalculationPerRender).ToArray();
            float height=mats[0].GetVector("_SCFrame").w;
            var sheet=new Texture2D(image.width*5,image.height*4,TextureFormat.RGB24,false);
            try
            {
                foreach(var skin in skins)skin.forceMatrixRecalculationPerRender=true;
                for(int row=0;row<4;row++)for(int column=0;column<5;column++)
                {
                    ShapeChangeSetup.SetShape(root,row<2?0:4,1,0,0);
                    foreach(var m in mats){m.SetFloat("_SCArmMorph",column*0.25f);m.SetFloat("_SCLegMorph",column*0.25f);m.SetFloat("_SCUpright",1);}
                    ShapeChangePose.Apply(source,root,true);
                    camera.orthographicSize=height*root.transform.lossyScale.y*0.65f;
                    camera.transform.position=root.transform.TransformPoint(new Vector3(row%2==0?0:height*3,height*0.4f,row%2==0?height*3:0));
                    camera.transform.LookAt(root.transform.TransformPoint(new Vector3(0,height*0.4f,0)));
                    camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,image.width,image.height),0,0);image.Apply();
                    sheet.SetPixels(column*image.width,(3-row)*image.height,image.width,image.height,image.GetPixels());
                }
                sheet.Apply();File.WriteAllBytes(Output+"/limb-morph-comparison.png",sheet.EncodeToPNG());
                report.AppendLine("Limb morph comparison: columns=0/0.25/0.5/0.75/1; rows=seated cube front/side, sphere front/side.");
            }
            finally
            {
                for(int i=0;i<mats.Length;i++){mats[i].SetFloat("_SCArmMorph",old[i].x);mats[i].SetFloat("_SCLegMorph",old[i].y);mats[i].SetFloat("_SCSeated",old[i].z);mats[i].SetFloat("_SCUpright",old[i].w);}
                for(int i=0;i<bones.Length;i++)bones[i].localRotation=rotations[i];
                for(int i=0;i<skins.Length;i++)skins[i].forceMatrixRecalculationPerRender=recalc[i];Object.DestroyImmediate(sheet);
            }
        }

        static void RenderPostureCases(GameObject root,Camera camera,RenderTexture rt,Texture2D image,StringBuilder report)
        {
            var source=SourceAvatar(root);var rig=root.GetComponent<Animator>();var mats=ShapeChangeSetup.Materials(root);
            var bones=ShapeChangePose.PoseBones.Select(rig.GetBoneTransform).ToArray();var rotations=bones.Select(b=>b.localRotation).ToArray();
            var values=mats.Select(m=>new Vector2(m.GetFloat("_SCUpright"),m.GetFloat("_SCSeated"))).ToArray();
            var frame=mats[0].GetVector("_SCFrame");float height=frame.w;
            var sheet=new Texture2D(image.width*4,image.height*4,TextureFormat.RGB24,false);
            var skins=root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var recalc=skins.Select(r=>r.forceMatrixRecalculationPerRender).ToArray();
            try
            {
                foreach(var skin in skins)skin.forceMatrixRecalculationPerRender=true;
                for(int row=0;row<4;row++)for(int shape=0;shape<2;shape++)
                {
                    ShapeChangeSetup.SetShape(root,shape==0?0:4,1,row>=2?0:1,row>=2?0:1);
                    foreach(var m in mats)m.SetFloat("_SCUpright",row==0?0:1);
                    ShapeChangePose.Apply(source,root,row==2);
                    for(int i=0;i<bones.Length;i++)
                    {
                        var original=source.GetBoneTransform(ShapeChangePose.PoseBones[i]);
                        if((bones[i].localPosition-original.localPosition).magnitude>0.00001f || (bones[i].localScale-original.localScale).magnitude>0.00001f)
                            throw new Exception("Pose stretched a bone: "+bones[i].name);
                    }
                    for(int side=0;side<2;side++)
                    {
                        float center=row==3?0.67f:0.4f;
                        camera.orthographicSize=height*root.transform.lossyScale.y*(row==3?0.8f:0.66f);
                        camera.transform.position=root.transform.TransformPoint(new Vector3(side==0?0:height*3,height*center,side==0?height*3:0));
                        camera.transform.LookAt(root.transform.TransformPoint(new Vector3(0,height*center,0)));
                        camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,image.width,image.height),0,0);image.Apply();
                        sheet.SetPixels((shape*2+side)*image.width,(3-row)*image.height,image.width,image.height,image.GetPixels());
                        string label=new[]{"legacy","upright","seated","standing"}[row]+"-"+(shape==0?"cube":"sphere")+"-"+(side==0?"front":"side");
                        File.WriteAllBytes(Output+"/posture-"+label+".png",image.EncodeToPNG());
                    }
                }
                sheet.Apply();File.WriteAllBytes(Output+"/posture-comparison.png",sheet.EncodeToPNG());
                report.AppendLine("PASS: real rig pose renders preserve bone lengths/scales. Posture comparison rows=legacy/upright/seated/T-pose standing; columns=cube front/side, sphere front/side.");
            }
            finally
            {
                for(int i=0;i<bones.Length;i++)bones[i].localRotation=rotations[i];
                for(int i=0;i<mats.Length;i++){mats[i].SetFloat("_SCUpright",values[i].x);mats[i].SetFloat("_SCSeated",values[i].y);}
                for(int i=0;i<skins.Length;i++)skins[i].forceMatrixRecalculationPerRender=recalc[i];
                Object.DestroyImmediate(sheet);
            }
        }

        static void RenderEarTailCases(GameObject root,Camera camera,RenderTexture rt,Texture2D image,StringBuilder report)
        {
            var renderers=root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>ShapeChangeSetup.IsEarOrTail(r.name)).ToArray();
            if(renderers.Length==0)return;
            var source=SourceAvatar(root);
            var meshes=renderers.Select(r=>r.sharedMesh).ToArray();var materials=renderers.Select(r=>r.sharedMaterials).ToArray();
            var temporary=new System.Collections.Generic.List<Object>();
            var frame=ShapeChangeSetup.Materials(root)[0].GetVector("_SCFrame");float height=frame.w;
            var sheet=new Texture2D(image.width*4,image.height*2,TextureFormat.RGB24,false);
            try
            {
                camera.orthographicSize=height*root.transform.lossyScale.y*0.55f;
                for(int keep=0;keep<2;keep++)
                {
                    for(int index=0;index<renderers.Length;index++)
                    {
                        var r=renderers[index];var original=source.transform.Find(AnimationUtility.CalculateTransformPath(r.transform,root.transform)).GetComponent<SkinnedMeshRenderer>();
                        var baked=new Mesh();original.BakeMesh(baked);
                        var matrix=source.transform.worldToLocalMatrix*original.transform.localToWorldMatrix;
                        var positions=baked.vertices.Select(matrix.MultiplyPoint3x4).ToArray();
                        var normals=baked.normals.Select(matrix.inverse.transpose.MultiplyVector).Select(n=>n.normalized).ToArray();Object.DestroyImmediate(baked);
                        r.sharedMesh=ShapeChangeSetup.PrepareMesh(source,original,positions,normals,frame,keep==1);temporary.Add(r.sharedMesh);
                        r.sharedMaterials=materials[index].Select(m=>
                        {
                            var copy=new Material(m);copy.SetFloat("_SCAccessory",keep);copy.SetFloat("_SCAppendage",1-keep);temporary.Add(copy);return copy;
                        }).ToArray();
                    }
                    if(keep==1)CheckAccessories(root,report);
                    for(int shape=0;shape<2;shape++)for(int side=0;side<2;side++)
                    {
                        ShapeChangeSetup.SetShape(root,shape==0?0:4,1);
                        camera.transform.position=root.transform.TransformPoint(new Vector3(side==0?0:height*3,height*0.4f,side==0?height*3:0));
                        camera.transform.LookAt(root.transform.TransformPoint(new Vector3(0,height*0.4f,0)));
                        camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,image.width,image.height),0,0);image.Apply();
                        sheet.SetPixels((shape*2+side)*image.width,(1-keep)*image.height,image.width,image.height,image.GetPixels());
                        File.WriteAllBytes(Output+"/ears-tail-"+(keep==1?"kept":"integrated")+"-"+(shape==0?"cube":"sphere")+"-"+(side==0?"front":"side")+".png",image.EncodeToPNG());
                    }
                }
                sheet.Apply();File.WriteAllBytes(Output+"/ears-tail-comparison.png",sheet.EncodeToPNG());
                report.AppendLine("Rendered BodyEar/BodyTail both integrated and shape-preserved: cube and sphere, front and side. Comparison top row=integrated, bottom row=kept; columns=cube front/side, sphere front/side.");
            }
            finally
            {
                for(int i=0;i<renderers.Length;i++){renderers[i].sharedMesh=meshes[i];renderers[i].sharedMaterials=materials[i];}
                foreach(var item in temporary)Object.DestroyImmediate(item);Object.DestroyImmediate(sheet);
            }
        }
    }
}
