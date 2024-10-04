using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using UnityEngine;

//-------------------------//

[RequireComponent(typeof(Camera))]
public class Raytracer : MonoBehaviour
{
	private const int WORKGROUP_SIZE_X = 8;
	private const int WORKGROUP_SIZE_Y = 8;
	
	[SerializeField]
	private ComputeShader m_shader;
	private RenderTexture m_outTexture = null;
	private Camera m_camera = null;

	//-------------------------//

	private ComputeBuffer m_tempBitmapBuffer;
	private ComputeBuffer m_tempVoxelDataBuffer;	
	private Vector3Int m_tempVolumeSize;

	//-------------------------//

	private void Awake()
	{
		m_camera = GetComponent<Camera>();
		m_camera.depthTextureMode = DepthTextureMode.Depth;

		m_tempVolumeSize = new Vector3Int(10, 10, 10);
		
		int bitmapSize = m_tempVolumeSize.x * m_tempVolumeSize.y * m_tempVolumeSize.z;
		bitmapSize = (bitmapSize + 31) & ~31; //align up to multiple of 32
		bitmapSize /= 32; //32 bits per uint32
		uint[] bitmap = new uint[bitmapSize];

		int voxelDataSize = m_tempVolumeSize.x * m_tempVolumeSize.y * m_tempVolumeSize.z;
		uint[] voxelData = new uint[voxelDataSize]; //1 uint32 per voxel (RGB color, 8 bits per component)

		for(int z = 0; z < m_tempVolumeSize.z; z++)
		for(int y = 0; y < m_tempVolumeSize.y; y++)
		for(int x = 0; x < m_tempVolumeSize.x; x++)
		{
			int idx = x + m_tempVolumeSize.x * (y + m_tempVolumeSize.y * z);

			float normX = (float)x / (float)m_tempVolumeSize.x * 2.0f - 1.0f;
			float normY = (float)y / (float)m_tempVolumeSize.y * 2.0f - 1.0f;
			float normZ = (float)z / (float)m_tempVolumeSize.z * 2.0f - 1.0f;

			if(normX * normX + normY * normY + normZ * normZ < 1.0f)
			{
				bitmap[idx / 32] |= 1u << (idx % 32);

				uint r = (uint)((normX * 0.5f + 0.5f) * 255.0f);
				uint g = (uint)((normY * 0.5f + 0.5f) * 255.0f);
				uint b = (uint)((normZ * 0.5f + 0.5f) * 255.0f);
				uint packedColor = (r << 24) | (g << 16) | (b << 8);

				voxelData[idx] = packedColor;
			}
			else
				bitmap[idx / 32] &= ~(1u << (idx % 32));
		}

		m_tempBitmapBuffer = new ComputeBuffer(bitmapSize, sizeof(uint));
		m_tempBitmapBuffer.SetData(bitmap);
		
		m_tempVoxelDataBuffer = new ComputeBuffer(voxelDataSize, sizeof(uint));
		m_tempVoxelDataBuffer.SetData(voxelData);
	}

	private void OnRenderImage(RenderTexture src, RenderTexture dst)
	{
		//recreate output texture if needed:
		//-----------------	
		if(m_outTexture == null || m_outTexture.width != Screen.width || m_outTexture.height != Screen.height)
		{
			if(m_outTexture)
				DestroyOutputTexture();

			CreateOutputTexture(Screen.width, Screen.height);
		}

		//set uniforms:
		//-----------------
		int[] outTextureDims = {m_outTexture.width, m_outTexture.height};
		m_shader.SetTexture(0, "u_outTexture", m_outTexture);
		m_shader.SetInts("u_outTextureDims", outTextureDims);

		m_shader.SetMatrix("u_invView", m_camera.cameraToWorldMatrix);
		m_shader.SetMatrix("u_invProj", m_camera.projectionMatrix.inverse);

		int[] volumeSize = {m_tempVolumeSize.x, m_tempVolumeSize.y, m_tempVolumeSize.z}; //temp
		m_shader.SetInts("u_volumeSize", volumeSize);
		m_shader.SetBuffer(0, "u_voxelBitmap", m_tempBitmapBuffer);
		m_shader.SetBuffer(0, "u_voxelData", m_tempVoxelDataBuffer);

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

	private void OnDestroy()
	{
		m_tempBitmapBuffer.Release();
		m_tempVoxelDataBuffer.Release();
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
