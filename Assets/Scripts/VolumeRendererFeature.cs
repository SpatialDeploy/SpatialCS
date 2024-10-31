using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

//-------------------------//

public class VoxelVolume
{
	public Vector3Int size;
	public Vector3Int sizeMap;

	public ComputeBuffer mapBuf;
	public ComputeBuffer brickBuf;
};

//-------------------------//

public class VolumeRendererFeature : ScriptableRendererFeature
{
    [SerializeField]
    private ComputeShader m_shader = null;
    private VolumeRenderPass m_renderPass;

    //-------------------------/

    private const int BRICK_SIZE = 8;
    public static VoxelVolume CreateVolume(Vector3Int size, byte[] brickmap)
	{
		if(size.x % BRICK_SIZE > 0 || size.y % BRICK_SIZE > 0 || size.z % BRICK_SIZE > 0)
		{
			Debug.LogWarning("volume size is not a multiple of BRICK_SIZE");
			return null;
		}

		VoxelVolume volume = new VoxelVolume();
		volume.size = new Vector3Int(size.x, size.z, size.y);
		volume.sizeMap = new Vector3Int(size.x / BRICK_SIZE, size.y / BRICK_SIZE, size.z / BRICK_SIZE);
		
		int mapBufSize = volume.sizeMap.x * volume.sizeMap.y * volume.sizeMap.z * sizeof(uint);
		int brickBufSize = brickmap.Length - mapBufSize;

		volume.mapBuf = new ComputeBuffer(mapBufSize, sizeof(uint));
		volume.mapBuf.SetData(brickmap, 0, 0, mapBufSize);

		volume.brickBuf = new ComputeBuffer(brickBufSize, sizeof(uint));
		volume.brickBuf.SetData(brickmap, mapBufSize, 0, brickBufSize);

		return volume;
	}

	public static void DestroyVolume(VoxelVolume volume)
	{
		volume.mapBuf.Release();
		volume.brickBuf.Release();
	}

    //-------------------------//

    public override void Create()
    {
        m_renderPass = new VolumeRenderPass(
            name: "Custom Volume Pass",
            shader: m_shader
        );
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if(m_shader == null || !SystemInfo.supportsComputeShaders)
            return;

        renderer.EnqueuePass(m_renderPass);
    }

	public void SetCurrentVolume(VoxelVolume volume)
	{
		m_renderPass.SetCurrentVolume(volume);
	}

    public void SetVolumeGameObject(GameObject gameObject)
    {
        m_renderPass.SetVolumeGameObject(gameObject);
    }

	//TEMP!!! JUST FOR DEMO
	public void SetCurrentTime(float t)
	{
		m_renderPass.SetCurrentTime(t);
	}

    class VolumeRenderPass : ScriptableRenderPass
    {
        private const int WORKGROUP_SIZE_X = 8;
        private const int WORKGROUP_SIZE_Y = 8;

        private readonly string m_profilerTag;
        private readonly ComputeShader m_shader;
        private GameObject m_volumeObject;
        private VoxelVolume m_curVolume;
        private float m_time = 0.5f; //TEMP!!! JUST FOR DEMO

        private RenderTexture m_outTexture;

        //-------------------------//

        public VolumeRenderPass(string name, ComputeShader shader)
        {
            m_profilerTag = name;
            m_shader = shader;
            
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }

        public void SetCurrentVolume(VoxelVolume volume)
        {
            m_curVolume = volume;
        }

        public void SetVolumeGameObject(GameObject gameObject)
        {
            m_volumeObject = gameObject;
        }

        //TEMP!!! JUST FOR DEMO
        public void SetCurrentTime(float t)
        {
            m_time = t;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;            
            CreateOutputTexture(descriptor.width, descriptor.height);
        }

        private void CreateOutputTexture(int width, int height)
        {
            if (m_outTexture != null && (m_outTexture.width != width || m_outTexture.height != height))
            {
                m_outTexture.Release();
                m_outTexture = null;
            }

            if (m_outTexture == null)
            {
                m_outTexture = new RenderTexture(width, height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
                m_outTexture.enableRandomWrite = true;
                m_outTexture.vrUsage = VRTextureUsage.TwoEyes;
                m_outTexture.Create();
            }
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if(m_curVolume == null) 
                return;

            CommandBuffer cmd = CommandBufferPool.Get(m_profilerTag);

            try
            {
                //get camera data:
                //-----------------
                renderingData.cameraData.cameraTargetDescriptor.enableRandomWrite = true;

                RTHandle colorHandle = renderingData.cameraData.renderer.cameraColorTargetHandle;
                RTHandle depthHandle = renderingData.cameraData.renderer.cameraDepthTargetHandle;

                Matrix4x4 invView, invProj;
                if(renderingData.cameraData.xrRendering)
                {
                    invView = renderingData.cameraData.xr.GetViewMatrix().inverse;
                    invProj = renderingData.cameraData.xr.GetProjMatrix().inverse;
                }
                else
                {
                    invView = renderingData.cameraData.camera.cameraToWorldMatrix;
                    invProj = renderingData.cameraData.camera.projectionMatrix.inverse;
                }
                
                //get volume transform matrix:
                //-----------------
                Matrix4x4 worldToLocal = Matrix4x4.identity;
                Matrix4x4 localToWorld = Matrix4x4.identity;
                if(m_volumeObject != null)
                {
                    Transform transform = m_volumeObject.transform;
                    if (transform != null)
                    {
                        worldToLocal = transform.worldToLocalMatrix;
                        localToWorld = transform.localToWorldMatrix;
                    }
                }
                
                //set uniforms:
                //-----------------
                int[] outTextureDims = { m_outTexture.width, m_outTexture.height };
                m_shader.SetTexture(0, "u_outTexture", m_outTexture);
                m_shader.SetInts("u_outTextureDims", outTextureDims);

                m_shader.SetMatrix("u_model", localToWorld);
                m_shader.SetMatrix("u_invModel", worldToLocal);
                m_shader.SetMatrix("u_invView", invView);
                m_shader.SetMatrix("u_invProj", invProj);
                
                int[] mapSize = { m_curVolume.sizeMap.x, m_curVolume.sizeMap.y, m_curVolume.sizeMap.z };
                m_shader.SetInts("u_mapSize", mapSize);
                m_shader.SetBuffer(0, "u_map", m_curVolume.mapBuf);
                m_shader.SetBuffer(0, "u_bricks", m_curVolume.brickBuf);

                m_shader.SetFloat("u_time", m_time); //TEMP!!! JUST FOR DEMO

                cmd.SetComputeTextureParam(m_shader, 0, "u_srcColorTexture", colorHandle);
                cmd.SetComputeTextureParam(m_shader, 0, "u_srcDepthTexture", depthHandle);

                int numWorkgroupsX = Mathf.CeilToInt((float)m_outTexture.width / WORKGROUP_SIZE_X);
                int numWorkgroupsY = Mathf.CeilToInt((float)m_outTexture.height / WORKGROUP_SIZE_Y);
                cmd.DispatchCompute(m_shader, 0, numWorkgroupsX, numWorkgroupsY, 1);

                cmd.Blit(m_outTexture, colorHandle);
            }
            finally
            {
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }
    }
}


