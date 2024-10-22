using System;
using System.IO;
using UnityEngine;

//-------------------------//

public class VoxelVolume
{
	public Vector3Int size;
	public Vector3Int sizeMap;


	public ComputeBuffer mapBuf;
	public ComputeBuffer brickBuf;
};

//-------------------------//

[RequireComponent(typeof(Camera))]
public class Raytracer : MonoBehaviour
{
	private const int WORKGROUP_SIZE_X = 8;
	private const int WORKGROUP_SIZE_Y = 8;
	private const int BRICK_SIZE = 5; //TODO: change to 8 (using 5 just to test sphere.vdb)
	
	[SerializeField]
	private ComputeShader m_shader = null;
	private RenderTexture m_outTexture = null;
	private Camera m_camera = null;
	[SerializeField]
	private GameObject m_volumeObject = null;

	private VoxelVolume m_curVolume = null;

	//-------------------------//

	public void SetCurrentVolume(VoxelVolume volume)
	{
		m_curVolume = volume;
	}

	//-------------------------//

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

	private void Awake()
	{
		m_camera = GetComponent<Camera>();
		m_camera.depthTextureMode = DepthTextureMode.Depth;
	}

	private void OnRenderImage(RenderTexture src, RenderTexture dst)
	{
		//dont render if no volume or shader is specified:
		//-----------------	
		if(m_shader == null || m_curVolume == null || !SystemInfo.supportsComputeShaders)
		{
			Graphics.Blit(src, dst);
			return;
		}

		//recreate output texture if needed:
		//-----------------	
		if(m_outTexture == null || m_outTexture.width != Screen.width || m_outTexture.height != Screen.height)
		{
			if(m_outTexture)
				DestroyOutputTexture();

			CreateOutputTexture(Screen.width, Screen.height);
		}

		//get volume transform matrix:
		//-----------------
		Matrix4x4 worldToLocal, localToWorld;
		if(m_volumeObject)
		{
			Transform transform = m_volumeObject.GetComponent<Transform>();
			if(transform)
			{
				worldToLocal = transform.worldToLocalMatrix;
				localToWorld = transform.localToWorldMatrix;
			}
			else
				worldToLocal = localToWorld = Matrix4x4.identity;
		}
		else
			worldToLocal = localToWorld = Matrix4x4.identity;

		//set uniforms:
		//-----------------
		int[] outTextureDims = {m_outTexture.width, m_outTexture.height};
		m_shader.SetTexture(0, "u_outTexture", m_outTexture);
		m_shader.SetInts("u_outTextureDims", outTextureDims);

		m_shader.SetMatrix("u_model", localToWorld);
		m_shader.SetMatrix("u_invModel", worldToLocal);
		m_shader.SetMatrix("u_invView", m_camera.cameraToWorldMatrix);
		m_shader.SetMatrix("u_invProj", m_camera.projectionMatrix.inverse);

		int[] mapSize = {m_curVolume.sizeMap.x, m_curVolume.sizeMap.y, m_curVolume.sizeMap.z};
		m_shader.SetInts("u_mapSize", mapSize);
		m_shader.SetBuffer(0, "u_map", m_curVolume.mapBuf);
		m_shader.SetBuffer(0, "u_bricks", m_curVolume.brickBuf);

		//TODO: in forward rendering mode the "_CameraDepthTexture" seems to lag one frame behind
		//figure out how to fix this in case we don't want to use deferred rendering

		m_shader.SetTexture(0, "u_srcColorTexture", src);
		m_shader.SetTextureFromGlobal(0, "u_srcDepthTexture", "_CameraDepthTexture");

		//dispatch and blit to target:
		//-----------------
		int numWorkgroupsX = Mathf.CeilToInt((float)m_outTexture.width / WORKGROUP_SIZE_X);
		int numWorkgroupsY = Mathf.CeilToInt((float)m_outTexture.height / WORKGROUP_SIZE_Y);
		m_shader.Dispatch(0, numWorkgroupsX, numWorkgroupsY, 1);

		Graphics.Blit(m_outTexture, dst);
	}

	//-------------------------//

	private void CreateOutputTexture(int width, int height)
	{
		m_outTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
		m_outTexture.enableRandomWrite = true;
		m_outTexture.vrUsage = VRTextureUsage.TwoEyes;
		m_outTexture.Create();
	}

	private void DestroyOutputTexture()
	{
		m_outTexture.Release();
	}
}
