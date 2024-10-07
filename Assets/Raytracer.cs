using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using UnityEngine;

//-------------------------//

public class VoxelVolume
{
	public Vector3Int size;
	public ComputeBuffer bitmapBuf;
	public ComputeBuffer voxelDataBuf;
};

//-------------------------//

[RequireComponent(typeof(Camera))]
public class Raytracer : MonoBehaviour
{
	private const int WORKGROUP_SIZE_X = 8;
	private const int WORKGROUP_SIZE_Y = 8;
	
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

	public static VoxelVolume CreateVolume(Vector3Int size, string[] voxels)
	{
		VoxelVolume volume = new VoxelVolume();
		volume.size = new Vector3Int(size.x, size.z, size.y);
		
		int bitmapSize = volume.size.x * volume.size.y * volume.size.z;
		bitmapSize = (bitmapSize + 31) & ~31; //align up to multiple of 32
		bitmapSize /= 32; //32 bits per uint32
		uint[] bitmap = new uint[bitmapSize];

		int voxelDataSize = volume.size.x * volume.size.y * volume.size.z;
		uint[] voxelData = new uint[voxelDataSize]; //1 uint32 per voxel (RGB color, 8 bits per component)

		for(int z = 0; z < volume.size.z; z++)
		for(int y = 0; y < volume.size.y; y++)
		for(int x = 0; x < volume.size.x; x++)
		{
			int idx = x + volume.size.x * (y + volume.size.y * z);
			int readIdx = x + volume.size.x * (z + volume.size.z * y);

			string voxel = voxels[readIdx];
			if(voxel != null)
			{
				bitmap[idx / 32] |= 1u << (idx % 32);

				uint r = Convert.ToUInt32(voxel.Substring(1, 2), 16);
				uint g = Convert.ToUInt32(voxel.Substring(3, 2), 16);
				uint b = Convert.ToUInt32(voxel.Substring(5, 2), 16);
				uint packedColor = (r << 24) | (g << 16) | (b << 8);

				voxelData[idx] = packedColor;
			}
			else
				bitmap[idx / 32] &= ~(1u << (idx % 32));
		}

		volume.bitmapBuf = new ComputeBuffer(bitmapSize, sizeof(uint));
		volume.bitmapBuf.SetData(bitmap);
		
		volume.voxelDataBuf = new ComputeBuffer(voxelDataSize, sizeof(uint));
		volume.voxelDataBuf.SetData(voxelData);

		return volume;
	}

	public static void DestroyVolume(VoxelVolume volume)
	{
		volume.bitmapBuf.Release();
		volume.voxelDataBuf.Release();
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
		if(m_shader == null || m_curVolume == null)
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

		int[] volumeSize = {m_curVolume.size.x, m_curVolume.size.y, m_curVolume.size.z}; //temp
		m_shader.SetInts("u_volumeSize", volumeSize);
		m_shader.SetBuffer(0, "u_voxelBitmap", m_curVolume.bitmapBuf);
		m_shader.SetBuffer(0, "u_voxelData", m_curVolume.voxelDataBuf);

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
		m_outTexture.Create();
	}

	private void DestroyOutputTexture()
	{
		m_outTexture.Release();
	}
}
