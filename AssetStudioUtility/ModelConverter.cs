﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AssetStudio
{
    public class ModelConverter : IImported
    {
        public ImportedFrame RootFrame { get; protected set; }
        public List<ImportedMesh> MeshList { get; protected set; } = new List<ImportedMesh>();
        public List<ImportedMaterial> MaterialList { get; protected set; } = new List<ImportedMaterial>();
        public List<ImportedTexture> TextureList { get; protected set; } = new List<ImportedTexture>();
        public List<ImportedKeyframedAnimation> AnimationList { get; protected set; } = new List<ImportedKeyframedAnimation>();
        public List<ImportedMorph> MorphList { get; protected set; } = new List<ImportedMorph>();

        private ImageFormat imageFormat;
        private Avatar avatar;
        private HashSet<AnimationClip> animationClipHashSet = new HashSet<AnimationClip>();
        private Dictionary<AnimationClip, string> boundAnimationPathDic = new Dictionary<AnimationClip, string>();
        private Dictionary<uint, string> bonePathHash = new Dictionary<uint, string>();
        private Dictionary<Texture2D, string> textureNameDictionary = new Dictionary<Texture2D, string>();
        private Dictionary<Transform, ImportedFrame> transformDictionary = new Dictionary<Transform, ImportedFrame>();
        Dictionary<uint, string> morphChannelNames = new Dictionary<uint, string>();

        public ModelConverter(GameObject m_GameObject, ImageFormat imageFormat, AnimationClip[] animationList = null)
        {
            this.imageFormat = imageFormat;
            if (m_GameObject.m_Animator != null)
            {
                InitWithAnimator(m_GameObject.m_Animator);
                if (animationList == null)
                {
                    CollectAnimationClip(m_GameObject.m_Animator);
                }
            }
            else
            {
                InitWithGameObject(m_GameObject);
            }
            if (animationList != null)
            {
                foreach (var animationClip in animationList)
                {
                    animationClipHashSet.Add(animationClip);
                }
            }
            ConvertAnimations();
        }

        public ModelConverter(string rootName, List<GameObject> m_GameObjects, ImageFormat imageFormat, AnimationClip[] animationList = null)
        {
            this.imageFormat = imageFormat;
            RootFrame = CreateFrame(rootName, Vector3.Zero, new Quaternion(0, 0, 0, 0), Vector3.One);
            foreach (var m_GameObject in m_GameObjects)
            {
                if (m_GameObject.m_Animator != null && animationList == null)
                {
                    CollectAnimationClip(m_GameObject.m_Animator);
                }

                var m_Transform = m_GameObject.m_Transform;
                ConvertTransforms(m_Transform, RootFrame);
                CreateBonePathHash(m_Transform);
            }
            foreach (var m_GameObject in m_GameObjects)
            {
                var m_Transform = m_GameObject.m_Transform;
                ConvertMeshRenderer(m_Transform);
            }
            if (animationList != null)
            {
                foreach (var animationClip in animationList)
                {
                    animationClipHashSet.Add(animationClip);
                }
            }
            ConvertAnimations();
        }

        public ModelConverter(Animator m_Animator, ImageFormat imageFormat, AnimationClip[] animationList = null)
        {
            this.imageFormat = imageFormat;
            InitWithAnimator(m_Animator);
            if (animationList == null)
            {
                CollectAnimationClip(m_Animator);
            }
            else
            {
                foreach (var animationClip in animationList)
                {
                    animationClipHashSet.Add(animationClip);
                }
            }
            ConvertAnimations();
        }

        private void InitWithAnimator(Animator m_Animator)
        {
            if (m_Animator.m_Avatar.TryGet(out var m_Avatar))
                avatar = m_Avatar;

            m_Animator.m_GameObject.TryGet(out var m_GameObject);
            InitWithGameObject(m_GameObject, m_Animator.m_HasTransformHierarchy);
        }

        private void InitWithGameObject(GameObject m_GameObject, bool hasTransformHierarchy = true)
        {
            var m_Transform = m_GameObject.m_Transform;
            if (!hasTransformHierarchy)
            {
                ConvertTransforms(m_Transform, null);
                DeoptimizeTransformHierarchy();
            }
            else
            {
                var frameList = new List<ImportedFrame>();
                var tempTransform = m_Transform;
                while (tempTransform.m_Father.TryGet(out var m_Father))
                {
                    frameList.Add(ConvertTransform(m_Father));
                    tempTransform = m_Father;
                }
                if (frameList.Count > 0)
                {
                    RootFrame = frameList[frameList.Count - 1];
                    for (var i = frameList.Count - 2; i >= 0; i--)
                    {
                        var frame = frameList[i];
                        var parent = frameList[i + 1];
                        parent.AddChild(frame);
                    }
                    ConvertTransforms(m_Transform, frameList[0]);
                }
                else
                {
                    ConvertTransforms(m_Transform, null);
                }

                CreateBonePathHash(m_Transform);
            }

            ConvertMeshRenderer(m_Transform);
        }

        private void ConvertMeshRenderer(Transform m_Transform)
        {
            m_Transform.m_GameObject.TryGet(out var m_GameObject);

            if (m_GameObject.m_MeshRenderer != null)
            {
                ConvertMeshRenderer(m_GameObject.m_MeshRenderer);
            }

            if (m_GameObject.m_SkinnedMeshRenderer != null)
            {
                ConvertMeshRenderer(m_GameObject.m_SkinnedMeshRenderer);
            }

            if (m_GameObject.m_Animation != null)
            {
                foreach (var animation in m_GameObject.m_Animation.m_Animations)
                {
                    if (animation.TryGet(out var animationClip))
                    {
                        if (!boundAnimationPathDic.ContainsKey(animationClip))
                        {
                            boundAnimationPathDic.Add(animationClip, GetTransformPath(m_Transform));
                        }
                        animationClipHashSet.Add(animationClip);
                    }
                }
            }

            foreach (var pptr in m_Transform.m_Children)
            {
                if (pptr.TryGet(out var child))
                    ConvertMeshRenderer(child);
            }
        }

        private void CollectAnimationClip(Animator m_Animator)
        {
            if (m_Animator.m_Controller.TryGet(out var m_Controller))
            {
                switch (m_Controller)
                {
                    case AnimatorOverrideController m_AnimatorOverrideController:
                        {
                            if (m_AnimatorOverrideController.m_Controller.TryGet<AnimatorController>(out var m_AnimatorController))
                            {
                                foreach (var pptr in m_AnimatorController.m_AnimationClips)
                                {
                                    if (pptr.TryGet(out var m_AnimationClip))
                                    {
                                        animationClipHashSet.Add(m_AnimationClip);
                                    }
                                }
                            }
                            break;
                        }

                    case AnimatorController m_AnimatorController:
                        {
                            foreach (var pptr in m_AnimatorController.m_AnimationClips)
                            {
                                if (pptr.TryGet(out var m_AnimationClip))
                                {
                                    animationClipHashSet.Add(m_AnimationClip);
                                }
                            }
                            break;
                        }
                }
            }
        }

        private ImportedFrame ConvertTransform(Transform trans)
        {
            var frame = new ImportedFrame(trans.m_Children.Length);
            transformDictionary.Add(trans, frame);
            trans.m_GameObject.TryGet(out var m_GameObject);
            frame.Name = m_GameObject.m_Name;
            SetFrame(frame, trans.m_LocalPosition, trans.m_LocalRotation, trans.m_LocalScale);
            return frame;
        }

        private static ImportedFrame CreateFrame(string name, Vector3 t, Quaternion q, Vector3 s)
        {
            var frame = new ImportedFrame();
            frame.Name = name;
            SetFrame(frame, t, q, s);
            return frame;
        }

        private static void SetFrame(ImportedFrame frame, Vector3 t, Quaternion q, Vector3 s)
        {
            throw new NotImplementedException("SetFrame is removed from this build of AssetStudio (this is a custom build for 'Clone Dash'). You can find a build with this functionality here: https://github.com/Perfare/AssetStudio");
        }

        private void ConvertTransforms(Transform trans, ImportedFrame parent)
        {
            var frame = ConvertTransform(trans);
            if (parent == null)
            {
                RootFrame = frame;
            }
            else
            {
                parent.AddChild(frame);
            }
            foreach (var pptr in trans.m_Children)
            {
                if (pptr.TryGet(out var child))
                    ConvertTransforms(child, frame);
            }
        }

        private void ConvertMeshRenderer(Renderer meshR)
        {
            var mesh = GetMesh(meshR);
            if (mesh == null)
                return;
            var iMesh = new ImportedMesh();
            meshR.m_GameObject.TryGet(out var m_GameObject2);
            iMesh.Path = GetTransformPath(m_GameObject2.m_Transform);
            iMesh.SubmeshList = new List<ImportedSubmesh>();
            var subHashSet = new HashSet<int>();
            var combine = false;
            int firstSubMesh = 0;
            if (meshR.m_StaticBatchInfo?.subMeshCount > 0)
            {
                firstSubMesh = meshR.m_StaticBatchInfo.firstSubMesh;
                var finalSubMesh = meshR.m_StaticBatchInfo.firstSubMesh + meshR.m_StaticBatchInfo.subMeshCount;
                for (int i = meshR.m_StaticBatchInfo.firstSubMesh; i < finalSubMesh; i++)
                {
                    subHashSet.Add(i);
                }
                combine = true;
            }
            else if (meshR.m_SubsetIndices?.Length > 0)
            {
                firstSubMesh = (int)meshR.m_SubsetIndices.Min(x => x);
                foreach (var index in meshR.m_SubsetIndices)
                {
                    subHashSet.Add((int)index);
                }
                combine = true;
            }

            iMesh.hasNormal = mesh.m_Normals?.Length > 0;
            iMesh.hasUV = new bool[8];
            for (int uv = 0; uv < 8; uv++)
            {
                iMesh.hasUV[uv] = mesh.GetUV(uv)?.Length > 0;
            }
            iMesh.hasTangent = mesh.m_Tangents != null && mesh.m_Tangents.Length == mesh.m_VertexCount * 4;
            iMesh.hasColor = mesh.m_Colors?.Length > 0;

            int firstFace = 0;
            for (int i = 0; i < mesh.m_SubMeshes.Length; i++)
            {
                int numFaces = (int)mesh.m_SubMeshes[i].indexCount / 3;
                if (subHashSet.Count > 0 && !subHashSet.Contains(i))
                {
                    firstFace += numFaces;
                    continue;
                }
                var submesh = mesh.m_SubMeshes[i];
                var iSubmesh = new ImportedSubmesh();
                Material mat = null;
                if (i - firstSubMesh < meshR.m_Materials.Length)
                {
                    if (meshR.m_Materials[i - firstSubMesh].TryGet(out var m_Material))
                    {
                        mat = m_Material;
                    }
                }
                ImportedMaterial iMat = ConvertMaterial(mat);
                iSubmesh.Material = iMat.Name;
                iSubmesh.BaseVertex = (int)mesh.m_SubMeshes[i].firstVertex;

                //Face
                iSubmesh.FaceList = new List<ImportedFace>(numFaces);
                var end = firstFace + numFaces;
                for (int f = firstFace; f < end; f++)
                {
                    var face = new ImportedFace();
                    face.VertexIndices = new int[3];
                    face.VertexIndices[0] = (int)(mesh.m_Indices[f * 3 + 2] - submesh.firstVertex);
                    face.VertexIndices[1] = (int)(mesh.m_Indices[f * 3 + 1] - submesh.firstVertex);
                    face.VertexIndices[2] = (int)(mesh.m_Indices[f * 3] - submesh.firstVertex);
                    iSubmesh.FaceList.Add(face);
                }
                firstFace = end;

                iMesh.SubmeshList.Add(iSubmesh);
            }

            // Shared vertex list
            iMesh.VertexList = new List<ImportedVertex>((int)mesh.m_VertexCount);
            for (var j = 0; j < mesh.m_VertexCount; j++)
            {
                var iVertex = new ImportedVertex();
                //Vertices
                int c = 3;
                if (mesh.m_Vertices.Length == mesh.m_VertexCount * 4)
                {
                    c = 4;
                }
                iVertex.Vertex = new Vector3(-mesh.m_Vertices[j * c], mesh.m_Vertices[j * c + 1], mesh.m_Vertices[j * c + 2]);
                //Normals
                if (iMesh.hasNormal)
                {
                    if (mesh.m_Normals.Length == mesh.m_VertexCount * 3)
                    {
                        c = 3;
                    }
                    else if (mesh.m_Normals.Length == mesh.m_VertexCount * 4)
                    {
                        c = 4;
                    }
                    iVertex.Normal = new Vector3(-mesh.m_Normals[j * c], mesh.m_Normals[j * c + 1], mesh.m_Normals[j * c + 2]);
                }
                //UV
                iVertex.UV = new float[8][];
                for (int uv = 0; uv < 8; uv++)
                {
                    if (iMesh.hasUV[uv])
                    {
                        var m_UV = mesh.GetUV(uv);
                        if (m_UV.Length == mesh.m_VertexCount * 2)
                        {
                            c = 2;
                        }
                        else if (m_UV.Length == mesh.m_VertexCount * 3)
                        {
                            c = 3;
                        }
                        iVertex.UV[uv] = new[] { m_UV[j * c], m_UV[j * c + 1] };
                    }
                }
                //Tangent
                if (iMesh.hasTangent)
                {
                    iVertex.Tangent = new Vector4(-mesh.m_Tangents[j * 4], mesh.m_Tangents[j * 4 + 1], mesh.m_Tangents[j * 4 + 2], mesh.m_Tangents[j * 4 + 3]);
                }
                //Colors
                if (iMesh.hasColor)
                {
                    if (mesh.m_Colors.Length == mesh.m_VertexCount * 3)
                    {
                        iVertex.Color = new Color(mesh.m_Colors[j * 3], mesh.m_Colors[j * 3 + 1], mesh.m_Colors[j * 3 + 2], 1.0f);
                    }
                    else
                    {
                        iVertex.Color = new Color(mesh.m_Colors[j * 4], mesh.m_Colors[j * 4 + 1], mesh.m_Colors[j * 4 + 2], mesh.m_Colors[j * 4 + 3]);
                    }
                }
                //BoneInfluence
                if (mesh.m_Skin?.Length > 0)
                {
                    var inf = mesh.m_Skin[j];
                    iVertex.BoneIndices = new int[4];
                    iVertex.Weights = new float[4];
                    for (var k = 0; k < 4; k++)
                    {
                        iVertex.BoneIndices[k] = inf.boneIndex[k];
                        iVertex.Weights[k] = inf.weight[k];
                    }
                }
                iMesh.VertexList.Add(iVertex);
            }

            if (meshR is SkinnedMeshRenderer sMesh)
            {
                //Bone
                /*
                 * 0 - None
                 * 1 - m_Bones
                 * 2 - m_BoneNameHashes
                 */
                var boneType = 0;
                if (sMesh.m_Bones.Length > 0)
                {
                    if (sMesh.m_Bones.Length == mesh.m_BindPose.Length)
                    {
                        var verifiedBoneCount = sMesh.m_Bones.Count(x => x.TryGet(out _));
                        if (verifiedBoneCount > 0)
                        {
                            boneType = 1;
                        }
                        if (verifiedBoneCount != sMesh.m_Bones.Length)
                        {
                            //尝试使用m_BoneNameHashes 4.3 and up
                            if (mesh.m_BindPose.Length > 0 && (mesh.m_BindPose.Length == mesh.m_BoneNameHashes?.Length))
                            {
                                //有效bone数量是否大于SkinnedMeshRenderer
                                var verifiedBoneCount2 = mesh.m_BoneNameHashes.Count(x => FixBonePath(GetPathFromHash(x)) != null);
                                if (verifiedBoneCount2 > verifiedBoneCount)
                                {
                                    boneType = 2;
                                }
                            }
                        }
                    }
                }
                if (boneType == 0)
                {
                    //尝试使用m_BoneNameHashes 4.3 and up
                    if (mesh.m_BindPose.Length > 0 && (mesh.m_BindPose.Length == mesh.m_BoneNameHashes?.Length))
                    {
                        var verifiedBoneCount = mesh.m_BoneNameHashes.Count(x => FixBonePath(GetPathFromHash(x)) != null);
                        if (verifiedBoneCount > 0)
                        {
                            boneType = 2;
                        }
                    }
                }

                if (boneType == 1)
                {
                    var boneCount = sMesh.m_Bones.Length;
                    iMesh.BoneList = new List<ImportedBone>(boneCount);
                    for (int i = 0; i < boneCount; i++)
                    {
                        var bone = new ImportedBone();
                        if (sMesh.m_Bones[i].TryGet(out var m_Transform))
                        {
                            bone.Path = GetTransformPath(m_Transform);
                        }
                        var convert = Matrix4x4.Scale(new Vector3(-1, 1, 1));
                        bone.Matrix = convert * mesh.m_BindPose[i] * convert;
                        iMesh.BoneList.Add(bone);
                    }
                }
                else if (boneType == 2)
                {
                    var boneCount = mesh.m_BindPose.Length;
                    iMesh.BoneList = new List<ImportedBone>(boneCount);
                    for (int i = 0; i < boneCount; i++)
                    {
                        var bone = new ImportedBone();
                        var boneHash = mesh.m_BoneNameHashes[i];
                        var path = GetPathFromHash(boneHash);
                        bone.Path = FixBonePath(path);
                        var convert = Matrix4x4.Scale(new Vector3(-1, 1, 1));
                        bone.Matrix = convert * mesh.m_BindPose[i] * convert;
                        iMesh.BoneList.Add(bone);
                    }
                }

                //Morphs
                if (mesh.m_Shapes?.channels?.Length > 0)
                {
                    var morph = new ImportedMorph();
                    MorphList.Add(morph);
                    morph.Path = iMesh.Path;
                    morph.Channels = new List<ImportedMorphChannel>(mesh.m_Shapes.channels.Length);
                    for (int i = 0; i < mesh.m_Shapes.channels.Length; i++)
                    {
                        var channel = new ImportedMorphChannel();
                        morph.Channels.Add(channel);
                        var shapeChannel = mesh.m_Shapes.channels[i];

                        var blendShapeName = "blendShape." + shapeChannel.name;
                        var crc = new SevenZip.CRC();
                        var bytes = Encoding.UTF8.GetBytes(blendShapeName);
                        crc.Update(bytes, 0, (uint)bytes.Length);
                        morphChannelNames[crc.GetDigest()] = blendShapeName;

                        channel.Name = shapeChannel.name.Split('.').Last();
                        channel.KeyframeList = new List<ImportedMorphKeyframe>(shapeChannel.frameCount);
                        var frameEnd = shapeChannel.frameIndex + shapeChannel.frameCount;
                        for (int frameIdx = shapeChannel.frameIndex; frameIdx < frameEnd; frameIdx++)
                        {
                            var keyframe = new ImportedMorphKeyframe();
                            channel.KeyframeList.Add(keyframe);
                            keyframe.Weight = mesh.m_Shapes.fullWeights[frameIdx];
                            var shape = mesh.m_Shapes.shapes[frameIdx];
                            keyframe.hasNormals = shape.hasNormals;
                            keyframe.hasTangents = shape.hasTangents;
                            keyframe.VertexList = new List<ImportedMorphVertex>((int)shape.vertexCount);
                            var vertexEnd = shape.firstVertex + shape.vertexCount;
                            for (uint j = shape.firstVertex; j < vertexEnd; j++)
                            {
                                var destVertex = new ImportedMorphVertex();
                                keyframe.VertexList.Add(destVertex);
                                var morphVertex = mesh.m_Shapes.vertices[j];
                                destVertex.Index = morphVertex.index;
                                var sourceVertex = iMesh.VertexList[(int)morphVertex.index];
                                destVertex.Vertex = new ImportedVertex();
                                var morphPos = morphVertex.vertex;
                                destVertex.Vertex.Vertex = sourceVertex.Vertex + new Vector3(-morphPos.X, morphPos.Y, morphPos.Z);
                                if (shape.hasNormals)
                                {
                                    var morphNormal = morphVertex.normal;
                                    destVertex.Vertex.Normal = new Vector3(-morphNormal.X, morphNormal.Y, morphNormal.Z);
                                }
                                if (shape.hasTangents)
                                {
                                    var morphTangent = morphVertex.tangent;
                                    destVertex.Vertex.Tangent = new Vector4(-morphTangent.X, morphTangent.Y, morphTangent.Z, 0);
                                }
                            }
                        }
                    }
                }
            }

            //TODO combine mesh
            if (combine)
            {
                meshR.m_GameObject.TryGet(out var m_GameObject);
                var frame = RootFrame.FindChild(m_GameObject.m_Name);
                if (frame != null)
                {
                    frame.LocalPosition = RootFrame.LocalPosition;
                    frame.LocalRotation = RootFrame.LocalRotation;
                    while (frame.Parent != null)
                    {
                        frame = frame.Parent;
                        frame.LocalPosition = RootFrame.LocalPosition;
                        frame.LocalRotation = RootFrame.LocalRotation;
                    }
                }
            }

            MeshList.Add(iMesh);
        }

        private static Mesh GetMesh(Renderer meshR)
        {
            if (meshR is SkinnedMeshRenderer sMesh)
            {
                if (sMesh.m_Mesh.TryGet(out var m_Mesh))
                {
                    return m_Mesh;
                }
            }
            else
            {
                meshR.m_GameObject.TryGet(out var m_GameObject);
                if (m_GameObject.m_MeshFilter != null)
                {
                    if (m_GameObject.m_MeshFilter.m_Mesh.TryGet(out var m_Mesh))
                    {
                        return m_Mesh;
                    }
                }
            }

            return null;
        }

        private string GetTransformPath(Transform transform)
        {
            if (transformDictionary.TryGetValue(transform, out var frame))
            {
                return frame.Path;
            }
            return null;
        }

        private string FixBonePath(AnimationClip m_AnimationClip, string path)
        {
            if (boundAnimationPathDic.TryGetValue(m_AnimationClip, out var basePath))
            {
                path = basePath + "/" + path;
            }
            return FixBonePath(path);
        }

        private string FixBonePath(string path)
        {
            var frame = RootFrame.FindFrameByPath(path);
            return frame?.Path;
        }

        private static string GetTransformPathByFather(Transform transform)
        {
            transform.m_GameObject.TryGet(out var m_GameObject);
            if (transform.m_Father.TryGet(out var father))
            {
                return GetTransformPathByFather(father) + "/" + m_GameObject.m_Name;
            }

            return m_GameObject.m_Name;
        }

        private ImportedMaterial ConvertMaterial(Material mat)
        {
            ImportedMaterial iMat;
            if (mat != null)
            {
                iMat = ImportedHelpers.FindMaterial(mat.m_Name, MaterialList);
                if (iMat != null)
                {
                    return iMat;
                }
                iMat = new ImportedMaterial();
                iMat.Name = mat.m_Name;
                //default values
                iMat.Diffuse = new Color(0.8f, 0.8f, 0.8f, 1);
                iMat.Ambient = new Color(0.2f, 0.2f, 0.2f, 1);
                iMat.Emissive = new Color(0, 0, 0, 1);
                iMat.Specular = new Color(0.2f, 0.2f, 0.2f, 1);
                iMat.Reflection = new Color(0, 0, 0, 1);
                iMat.Shininess = 20f;
                iMat.Transparency = 0f;
                foreach (var col in mat.m_SavedProperties.m_Colors)
                {
                    switch (col.Key)
                    {
                        case "_Color":
                            iMat.Diffuse = col.Value;
                            break;
                        case "_SColor":
                            iMat.Ambient = col.Value;
                            break;
                        case "_EmissionColor":
                            iMat.Emissive = col.Value;
                            break;
                        case "_SpecularColor":
                            iMat.Specular = col.Value;
                            break;
                        case "_ReflectColor":
                            iMat.Reflection = col.Value;
                            break;
                    }
                }

                foreach (var flt in mat.m_SavedProperties.m_Floats)
                {
                    switch (flt.Key)
                    {
                        case "_Shininess":
                            iMat.Shininess = flt.Value;
                            break;
                        case "_Transparency":
                            iMat.Transparency = flt.Value;
                            break;
                    }
                }

                //textures
                iMat.Textures = new List<ImportedMaterialTexture>();
                foreach (var texEnv in mat.m_SavedProperties.m_TexEnvs)
                {
                    if (!texEnv.Value.m_Texture.TryGet<Texture2D>(out var m_Texture2D)) //TODO other Texture
                    {
                        continue;
                    }

                    var texture = new ImportedMaterialTexture();
                    iMat.Textures.Add(texture);

                    int dest = -1;
                    if (texEnv.Key == "_MainTex")
                        dest = 0;
                    else if (texEnv.Key == "_BumpMap")
                        dest = 3;
                    else if (texEnv.Key.Contains("Specular"))
                        dest = 2;
                    else if (texEnv.Key.Contains("Normal"))
                        dest = 1;

                    texture.Dest = dest;

                    var ext = $".{imageFormat.ToString().ToLower()}";
                    if (textureNameDictionary.TryGetValue(m_Texture2D, out var textureName))
                    {
                        texture.Name = textureName;
                    }
                    else if (ImportedHelpers.FindTexture(m_Texture2D.m_Name + ext, TextureList) != null) //已有相同名字的图片
                    {
                        for (int i = 1; ; i++)
                        {
                            var name = m_Texture2D.m_Name + $" ({i}){ext}";
                            if (ImportedHelpers.FindTexture(name, TextureList) == null)
                            {
                                texture.Name = name;
                                textureNameDictionary.Add(m_Texture2D, name);
                                break;
                            }
                        }
                    }
                    else
                    {
                        texture.Name = m_Texture2D.m_Name + ext;
                        textureNameDictionary.Add(m_Texture2D, texture.Name);
                    }

                    texture.Offset = texEnv.Value.m_Offset;
                    texture.Scale = texEnv.Value.m_Scale;
                    ConvertTexture2D(m_Texture2D, texture.Name);
                }

                MaterialList.Add(iMat);
            }
            else
            {
                iMat = new ImportedMaterial();
            }
            return iMat;
        }

        private void ConvertTexture2D(Texture2D m_Texture2D, string name)
        {
            var iTex = ImportedHelpers.FindTexture(name, TextureList);
            if (iTex != null)
            {
                return;
            }

            var stream = m_Texture2D.ConvertToStream(imageFormat, true);
            if (stream != null)
            {
                using (stream)
                {
                    iTex = new ImportedTexture(stream, name);
                    TextureList.Add(iTex);
                }
            }
        }

        private void ConvertAnimations()
        {
            throw new NotImplementedException("ConvertAnimations is removed from this build of AssetStudio (this is a custom build for 'Clone Dash'). You can find a build with this functionality here: https://github.com/Perfare/AssetStudio");
        }

        private void ReadCurveData(ImportedKeyframedAnimation iAnim, AnimationClipBindingConstant m_ClipBindingConstant, int index, float time, float[] data, int offset, ref int curveIndex)
        {
            throw new NotImplementedException("ReadCurveData is removed from this build of AssetStudio (this is a custom build for 'Clone Dash'). You can find a build with this functionality here: https://github.com/Perfare/AssetStudio");
        }

        private string GetPathFromHash(uint hash)
        {
            bonePathHash.TryGetValue(hash, out var boneName);
            if (string.IsNullOrEmpty(boneName))
            {
                boneName = avatar?.FindBonePath(hash);
            }
            if (string.IsNullOrEmpty(boneName))
            {
                boneName = "unknown " + hash;
            }
            return boneName;
        }

        private void CreateBonePathHash(Transform m_Transform)
        {
            var name = GetTransformPathByFather(m_Transform);
            var crc = new SevenZip.CRC();
            var bytes = Encoding.UTF8.GetBytes(name);
            crc.Update(bytes, 0, (uint)bytes.Length);
            bonePathHash[crc.GetDigest()] = name;
            int index;
            while ((index = name.IndexOf("/", StringComparison.Ordinal)) >= 0)
            {
                name = name.Substring(index + 1);
                crc = new SevenZip.CRC();
                bytes = Encoding.UTF8.GetBytes(name);
                crc.Update(bytes, 0, (uint)bytes.Length);
                bonePathHash[crc.GetDigest()] = name;
            }
            foreach (var pptr in m_Transform.m_Children)
            {
                if (pptr.TryGet(out var child))
                    CreateBonePathHash(child);
            }
        }

        private void DeoptimizeTransformHierarchy()
        {
            if (avatar == null)
                throw new Exception("Transform hierarchy has been optimized, but can't find Avatar to deoptimize.");
            // 1. Figure out the skeletonPaths from the unstripped avatar
            var skeletonPaths = new List<string>();
            foreach (var id in avatar.m_Avatar.m_AvatarSkeleton.m_ID)
            {
                var path = avatar.FindBonePath(id);
                skeletonPaths.Add(path);
            }
            // 2. Restore the original transform hierarchy
            // Prerequisite: skeletonPaths follow pre-order traversal
            for (var i = 1; i < skeletonPaths.Count; i++) // start from 1, skip the root transform because it will always be there.
            {
                var path = skeletonPaths[i];
                var strs = path.Split('/');
                string transformName;
                ImportedFrame parentFrame;
                if (strs.Length == 1)
                {
                    transformName = path;
                    parentFrame = RootFrame;
                }
                else
                {
                    transformName = strs.Last();
                    var parentFramePath = path.Substring(0, path.LastIndexOf('/'));
                    parentFrame = RootFrame.FindRelativeFrameWithPath(parentFramePath);
                }
                var skeletonPose = avatar.m_Avatar.m_DefaultPose;
                var xform = skeletonPose.m_X[i];
                var frame = RootFrame.FindChild(transformName);
                if (frame != null)
                {
                    SetFrame(frame, xform.t, xform.q, xform.s);
                }
                else
                {
                    frame = CreateFrame(transformName, xform.t, xform.q, xform.s);
                }
                parentFrame.AddChild(frame);
            }
        }

        private string GetPathByChannelName(string channelName)
        {
            foreach (var morph in MorphList)
            {
                foreach (var channel in morph.Channels)
                {
                    if (channel.Name == channelName)
                    {
                        return morph.Path;
                    }
                }
            }
            return null;
        }

        private string GetChannelNameFromHash(uint attribute)
        {
            if (morphChannelNames.TryGetValue(attribute, out var name))
            {
                return name;
            }
            else
            {
                return null;
            }
        }
    }
}
