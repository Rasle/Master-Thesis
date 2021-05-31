using System;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

namespace SIM
{
    [ExecuteInEditMode]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [AddComponentMenu("")]
    public class FluidRender : MonoBehaviour
    {
        void OnEnable()
        {
            Create();
        }

        void OnDisable()
        {
            Destroy();
        }

        class CameraCommands
        {
            public CommandBuffer copyBackground;
            public List<RenderTexture> fluidDepths = new List<RenderTexture>();
        }
        static Dictionary<Camera, CameraCommands> sm_cameraCommands = new Dictionary<Camera, CameraCommands>();
        static CameraEvent CAMERA_EVENT = CameraEvent.BeforeForwardAlpha;
        static void RemoveCommandBuffer(Camera cam)
        {
            if (sm_cameraCommands.ContainsKey(cam))
            {
                var cameraCommands = sm_cameraCommands[cam];
                if (cameraCommands.fluidDepths.Count > 0)
                {
                    foreach (var cb in cameraCommands.fluidDepths) RenderTexture.ReleaseTemporary(cb);
                    cameraCommands.fluidDepths.Clear();
                }
                else
                {
                    cam.RemoveCommandBuffer(CAMERA_EVENT, cameraCommands.copyBackground);
                    sm_cameraCommands.Remove(cam);
                    if (sm_cameraCommands.Count == 0) Camera.onPostRender -= RemoveCommandBuffer;
                }
            }
        }

        void OnWillRenderObject()
        {
            var cam = Camera.current;

            if (cam.cameraType == CameraType.Preview) return;

            if (!sm_cameraCommands.ContainsKey(cam))
            {
                var copyBackground = new CommandBuffer();
                copyBackground.name = "Copy fluid background";
                int fluidBackgroundID = Shader.PropertyToID("_FluidBackground");
                copyBackground.GetTemporaryRT(fluidBackgroundID, -1, -1, 0);
                copyBackground.Blit(BuiltinRenderTextureType.CurrentActive, fluidBackgroundID);
                cam.AddCommandBuffer(CAMERA_EVENT, copyBackground);
                var cameraCommands = new CameraCommands();
                cameraCommands.copyBackground = copyBackground;
                sm_cameraCommands.Add(cam, cameraCommands);
                if (sm_cameraCommands.Count == 1) Camera.onPostRender += RemoveCommandBuffer;
            }

            int depthPass = m_prepareFluidMaterial.FindPass("FluidDepth".ToUpper());
            int depthBlurPass = m_prepareFluidMaterial.FindPass("FluidDepthBlur".ToUpper());
            if (depthPass != -1 && depthBlurPass != -1)
            {
                RenderTexture active = RenderTexture.active;

                // Depth texture
                RenderTextureDescriptor depthDesc = new RenderTextureDescriptor(cam.pixelWidth, cam.pixelHeight);
                depthDesc.colorFormat = RenderTextureFormat.RGFloat;
                depthDesc.depthBufferBits = 24;
                RenderTexture depth = RenderTexture.GetTemporary(depthDesc);
                Graphics.SetRenderTarget(depth);
                GL.Clear(true, true, new Color(cam.farClipPlane, cam.farClipPlane, 0, 0), 1.0f);
                m_prepareFluidMaterial.SetBuffer("_Indices", m_indexBuffer);
                m_prepareFluidMaterial.SetBuffer("_Positions", m_positionBuffer);
                if (cam.stereoActiveEye != Camera.MonoOrStereoscopicEye.Mono)
                {
                    m_prepareFluidMaterial.SetMatrixArray("_ViewMatrix", new[] { cam.GetStereoViewMatrix(Camera.StereoscopicEye.Left), cam.GetStereoViewMatrix(Camera.StereoscopicEye.Right) });
                    m_prepareFluidMaterial.SetMatrixArray("_ProjMatrix", new[] { GL.GetGPUProjectionMatrix(cam.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left), active != null), GL.GetGPUProjectionMatrix(cam.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right), active != null) });
                    m_prepareFluidMaterial.SetInt("_EyeCount", 2);
                    m_prepareFluidMaterial.SetPass(depthPass);
                    Graphics.DrawProceduralNow(MeshTopology.Points, m_scene.num_par);
                }
                else
                {
                    m_prepareFluidMaterial.SetMatrixArray("_ViewMatrix", new[] { cam.worldToCameraMatrix, Matrix4x4.identity });
                    m_prepareFluidMaterial.SetMatrixArray("_ProjMatrix", new[] { GL.GetGPUProjectionMatrix(cam.projectionMatrix, active != null), Matrix4x4.identity });
                    m_prepareFluidMaterial.SetInt("_EyeCount", 1);
                    m_prepareFluidMaterial.SetPass(depthPass);
                    Graphics.DrawProceduralNow(MeshTopology.Points, m_scene.num_par);
                }
                Graphics.SetRenderTarget(active);

                // Blur texture
                RenderTextureDescriptor depthBlurDesc = new RenderTextureDescriptor(cam.pixelWidth, cam.pixelHeight);
                depthBlurDesc.colorFormat = RenderTextureFormat.RGFloat;
                depthBlurDesc.depthBufferBits = 0;
                RenderTexture depthBlur = RenderTexture.GetTemporary(depthBlurDesc);
                Graphics.SetRenderTarget(depthBlur);
                GL.Clear(false, true, new Color(cam.farClipPlane, cam.farClipPlane, 0, 0));
                m_prepareFluidMaterial.SetFloat("_FarPlane", cam.farClipPlane);
                m_prepareFluidMaterial.SetVector("_InvScreen", new Vector2(1.0f / cam.pixelWidth, 1.0f / cam.pixelHeight));
                m_prepareFluidMaterial.SetTexture("_DepthTex", depth);
                Graphics.Blit(null, m_prepareFluidMaterial, depthBlurPass);

                RenderTexture.ReleaseTemporary(depth);
                sm_cameraCommands[cam].fluidDepths.Add(depthBlur);

                m_fluidMaterial.SetTexture("_DepthTex", depthBlur);
                m_fluidMaterial.SetFloat("FLEX_FLIP_Y", active ? 1.0f : 0.0f);
            }
        }

        //const string PREPARE_FLUID_SHADER = "Custom/RenderFluid";
        const string PREPARE_FLUID_SHADER = "Flex/FlexPrepareFluid";

        void Create()
        {
            GameObject tmp = GameObject.Find("Main Camera");
            m_scene = tmp.GetComponent<MPMBurst>();
            if (m_scene == null)
            {
                Debug.LogError("Scene is null");
                Debug.Break();
            }

            if (m_scene)
            {
                m_mesh = new Mesh
                {
                    name = "Flex Fluid",
                    vertices = new Vector3[1]
                };
                m_mesh.SetIndices(new int[0], MeshTopology.Points, 0);

                m_prepareFluidMaterial = new Material(Shader.Find(PREPARE_FLUID_SHADER));
                m_prepareFluidMaterial.hideFlags = HideFlags.HideAndDontSave;

                m_fluidMaterial = m_scene.fluid_material;

                MeshFilter meshFilter = GetComponent<MeshFilter>();
                meshFilter.mesh = m_mesh;

                MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
                meshRenderer.material = m_fluidMaterial;
                meshRenderer.shadowCastingMode = ShadowCastingMode.On;
                meshRenderer.receiveShadows = true;
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetSelectedRenderState(meshRenderer, UnityEditor.EditorSelectedRenderState.Hidden);
#endif

                int maxParticles = m_scene.num_par;
                m_positionBuffer = new ComputeBuffer(maxParticles, sizeof(float) * 4);
            }
        }

        void Destroy()
        {
            if (m_mesh) DestroyImmediate(m_mesh);
            if (m_prepareFluidMaterial) DestroyImmediate(m_prepareFluidMaterial);
            if (m_positionBuffer != null) { m_positionBuffer.Release(); m_positionBuffer = null; }
            if (m_indexBuffer != null) m_indexBuffer.Release();
        }

        public void UpdateMesh()
        {
            transform.rotation = Quaternion.identity;

            if (m_scene && m_fluidMaterial)
            {
                int maxParticles = m_scene.num_par;
                int[] indices = m_scene.indices;
                int indexCount = m_scene.num_par;
                if (m_indexBuffer != null && indexCount != m_indexBuffer.count)
                {
                    m_indexBuffer.Release();
                    m_indexBuffer = null;
                }
                if (m_indexBuffer == null && indexCount > 0)
                {
                    m_indexBuffer = new ComputeBuffer(indexCount, sizeof(int));
                    m_mesh.SetIndices(new int[indexCount], MeshTopology.Points, 0);
                }
                if (m_indexBuffer != null)
                {
                    m_indexBuffer.SetData(indices);
                }
                Vector3 boundsMin = Vector3.zero, boundsMax = new Vector3(m_scene.grid_res, m_scene.grid_res, m_scene.grid_res);
                if (maxParticles > 0)
                {
                    m_scene.GetParticles(m_positionBuffer);

                    m_fluidMaterial.SetBuffer("_Points", m_positionBuffer);
                    m_fluidMaterial.SetBuffer("_Indices", m_indexBuffer);
                }

                Vector3 center = (boundsMin + boundsMax) * 0.5f;
                Vector3 size = boundsMax - boundsMin;
                m_bounds.center = Vector3.zero;
                m_bounds.size = size;
                m_bounds.Expand(m_scene.particle_radius);
                m_mesh.bounds = m_bounds;
                transform.position = center;
            }
        }

        MPMBurst m_scene;
        Mesh m_mesh;
        Bounds m_bounds = new Bounds();

        [NonSerialized]
        Material m_fluidMaterial;
        [NonSerialized]
        Material m_prepareFluidMaterial;
        [NonSerialized]
        ComputeBuffer m_positionBuffer, m_indexBuffer;
    }
}