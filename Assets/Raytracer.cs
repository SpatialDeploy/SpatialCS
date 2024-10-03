using System;
using System.Collections;
using System.Collections.Generic;
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

	private void Awake()
	{
		m_camera = GetComponent<Camera>();
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

		int[] volumeSize = {10, 10, 10}; //temp
		m_shader.SetInts("u_volumeSize", volumeSize);

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
