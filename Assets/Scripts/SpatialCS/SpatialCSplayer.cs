using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SPLVnative;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Networking;

//-------------------------//

public class Spatial
{
	public Vector3Int size;
	public float framerate;
	public int framecount;
	public float duration;
	
	public byte[][] frames;
};

//-------------------------//

//for the web request on android
public static class ExtensionMethods
{
	public static TaskAwaiter<UnityWebRequest.Result> GetAwaiter(this UnityWebRequestAsyncOperation operation)
	{
		var tcs = new TaskCompletionSource<UnityWebRequest.Result>();
		operation.completed += obj => { tcs.SetResult(operation.webRequest.result); };
		return tcs.Task.GetAwaiter();
	}
}

//-------------------------//

public class SpatialCSplayer : MonoBehaviour
{
	[SerializeField]
	private string spatialName;

	[SerializeField]
	private SpatialCSrendererFeature m_renderFeature;
	private SpatialCSdecoder m_decoder;
	private SpatialCSmetadata m_metadata;
	private SpatialCSrendererFeature.VoxelVolume m_curVolume = null;

	
	[SerializeField]
	private Material m_boundingBoxMaterial = null;
	private const float BOUNDING_BOX_WIDTH = 0.005f;
	private readonly Vector3[] m_boundingBoxCorners = new Vector3[8];
	private readonly LineRenderer[] m_lineRenderers = new LineRenderer[12];
	private float m_targetLineWidth = 0.0f;
	private float m_lineWidth = 0.0f;

	private float m_curTime = 0.0f;
	private int m_curFrame = -1;
	private uint m_decodingFrame = 0;
	private bool m_isDecodingFrame = false;

	private bool m_isPlaying = false;
	private bool m_adjustingProgress = false;

	//-------------------------//

	public void PauseButtonClicked()
	{
		m_isPlaying = !m_isPlaying;
	}

	public bool IsPlaying()
	{
		return m_isPlaying;
	}

	public void SetProgress(float val)
	{
		if(m_decoder == null)
			return;

		m_curTime = val * m_metadata.duration;
	}

	public void SetAdjustingProgess(bool adjusting)
	{
		m_adjustingProgress = adjusting;
	}

	public float GetProgress()
	{
		if(m_decoder == null)
			return 0.0f;

		return (m_curTime / m_metadata.duration) % 1.0f;
	}

	public Vector3Int GetSpatialResolution()
	{
		if(m_decoder == null)
			return new Vector3Int(1, 1, 1);

		return new Vector3Int((int)m_metadata.width, (int)m_metadata.depth, (int)m_metadata.height);
	}

	//-------------------------//

	private async void Start()
	{
		//create decoder:
		//-----------------
		string spatialPath = Path.Combine(Application.streamingAssetsPath, spatialName);

		//on android, StreamingAssets gets compressed and packaged strangely, we need to copy to a normal file
		if(Application.platform == RuntimePlatform.Android)
		{
			using(UnityWebRequest www = UnityWebRequest.Get(spatialPath))
			{
				UnityWebRequest.Result result = await www.SendWebRequest();
				if(result == UnityWebRequest.Result.Success)
				{
					string extractedPath = Path.Combine(Application.persistentDataPath, spatialName);
					string directoryPath = Path.GetDirectoryName(extractedPath);
					if(!string.IsNullOrEmpty(directoryPath))
						Directory.CreateDirectory(directoryPath);

					File.WriteAllBytes(extractedPath, www.downloadHandler.data);
					spatialPath = extractedPath;
				}
				else
				{
					Debug.LogError($"failed to load file: {www.error}");
					return;
				}
			}
		}

		try
		{
			m_decoder = new SpatialCSdecoder(spatialPath);
			m_metadata = m_decoder.GetMetadata();
		}
		catch(Exception e)
		{
			Debug.LogError($"failed to initialize decoder: {e.Message}");
			m_decoder = null;
		}

		//setup playback params:
		//-----------------
		m_isPlaying = true;
		m_curTime = 0.0f;
		m_curFrame = -1;
		m_curVolume = null;
		m_isDecodingFrame = false;

		//setup renderer:
		//-----------------
		if(m_renderFeature != null)
			m_renderFeature.SetVolumeGameObject(transform.gameObject);

		//setup bounding box:
		//-----------------
		SetupBoundingBox();

		//start decoding first frame:
		//-----------------
		if(m_decoder != null)
		{
			m_decodingFrame = m_decoder.GetClosestDecodableFrameIdx(0);
			m_decoder.StartDecodingFrame(m_decodingFrame);
			m_isDecodingFrame = true;
		}
	}

	private void Update()
	{
		//skip if decoder wasnt created or raytracer wasnt found:
		//-----------------
		if(m_decoder == null || m_renderFeature == null)
			return;

		//update current time:
		//-----------------
		if(m_isPlaying && !m_adjustingProgress)
			m_curTime += Time.deltaTime;

		//get newly decoded frame, if it exists:
		//-----------------
		if(m_isDecodingFrame && m_decoder.TryGetDecodedFrame(out SpatialCSframeRef decodedFrame))
		{
			//destroy old volume
			if(m_curVolume != null)
				SpatialCSrendererFeature.DestroyVolume(m_curVolume);

			//get handles to unmanaged memory
			SPLVframe frameStruct = Marshal.PtrToStructure<SPLVframe>(decodedFrame.frame);
			uint mapBufSize = frameStruct.width * frameStruct.height * frameStruct.depth * sizeof(uint);
			uint brickBufSize = frameStruct.bricksLen * (uint)Marshal.SizeOf<SPLVbrick>();

			NativeArray<byte> mapBuf;
			NativeArray<byte> brickBuf;
			unsafe
			{
				mapBuf   = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>((void*)frameStruct.map   , (int)mapBufSize  , Allocator.None);
				brickBuf = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>((void*)frameStruct.bricks, (int)brickBufSize, Allocator.None);
			}

			//create new volume
		#if ENABLE_UNITY_COLLECTIONS_CHECKS
			AtomicSafetyHandle mapSafetyHandle = AtomicSafetyHandle.Create();
			AtomicSafetyHandle brickSafetyHandle = AtomicSafetyHandle.Create();

			NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref mapBuf, mapSafetyHandle);
			NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref brickBuf, brickSafetyHandle);
		#endif

			m_curVolume = SpatialCSrendererFeature.CreateVolume(
				new Vector3Int((int)frameStruct.width, (int)frameStruct.height, (int)frameStruct.depth),
				mapBuf, brickBuf
			);

		#if ENABLE_UNITY_COLLECTIONS_CHECKS
			AtomicSafetyHandle.Release(mapSafetyHandle);
			AtomicSafetyHandle.Release(brickSafetyHandle);
		#endif

			//set current frame
			m_renderFeature.SetCurrentVolume(m_curVolume);
			m_curFrame = (int)m_decodingFrame;
			m_isDecodingFrame = false;

			//free frame
			mapBuf.Dispose();
			brickBuf.Dispose();
			m_decoder.FreeFrame(decodedFrame);
		}

		//start decoding next frame, if neccesary:
		//-----------------
		int nextFrame = (int)(m_curTime * m_metadata.framerate);
		nextFrame %= (int)m_metadata.framecount;

		if(m_curFrame != nextFrame && !m_isDecodingFrame)
		{
			m_decodingFrame = m_decoder.GetClosestDecodableFrameIdx((uint)nextFrame);
			m_decoder.StartDecodingFrame(m_decodingFrame);
			m_isDecodingFrame = true;
		}

		//draw bounding box:
		//-----------------
		DrawBoundingBox();
	}

	private void OnDestroy()
	{
		if(m_renderFeature != null)
			m_renderFeature.SetCurrentVolume(null);

		if(m_curVolume != null)
			SpatialCSrendererFeature.DestroyVolume(m_curVolume);

		m_decoder = null;
	}

	public void OnHover()
	{
		m_targetLineWidth = BOUNDING_BOX_WIDTH;
	}

	public void OnUnHover()
	{
		m_targetLineWidth = 0.0f;
	}

	private void SetupBoundingBox()
	{
		//init variables:
		//-----------------
		m_targetLineWidth = 0.0f;
		m_lineWidth = m_targetLineWidth;

		//create line renderers:
		//-----------------
		if(m_boundingBoxMaterial == null)
			m_boundingBoxMaterial = new Material(Shader.Find("Sprites/Default"));
		else
		{
			m_boundingBoxMaterial = new Material(m_boundingBoxMaterial);
			m_boundingBoxMaterial.SetColor("_Color", Color.black);
		}

		GameObject linesParent = new GameObject("BoundingBoxLines");
		linesParent.transform.SetParent(transform);
		for(int i = 0; i < 12; i++)
		{
			GameObject lineObj = new GameObject($"Line_{i}");
			lineObj.transform.SetParent(linesParent.transform);
			
			LineRenderer line = lineObj.AddComponent<LineRenderer>();
			line.positionCount = 2;
			line.startWidth = m_lineWidth;
			line.endWidth = m_lineWidth;
			line.material = m_boundingBoxMaterial;
			line.startColor = Color.black;
			line.endColor = Color.black;

			m_lineRenderers[i] = line;
		}

		//generate line vertex positions:
		//-----------------
		Vector3Int spatialSize = GetSpatialResolution();
		int maxSize = Math.Max(Math.Max(spatialSize.x, spatialSize.y), spatialSize.z);

		Vector3 minBounds = new Vector3(-(float)spatialSize.x / maxSize / 2.0f, -(float)spatialSize.y / maxSize / 2.0f, -(float)spatialSize.z / maxSize / 2.0f);
		Vector3 maxBounds = new Vector3( (float)spatialSize.x / maxSize / 2.0f,  (float)spatialSize.y / maxSize / 2.0f,  (float)spatialSize.z / maxSize / 2.0f);

		m_boundingBoxCorners[0] = new Vector3(minBounds.x, minBounds.y, minBounds.z);
		m_boundingBoxCorners[1] = new Vector3(minBounds.x, minBounds.y, maxBounds.z);
		m_boundingBoxCorners[2] = new Vector3(minBounds.x, maxBounds.y, minBounds.z);
		m_boundingBoxCorners[3] = new Vector3(minBounds.x, maxBounds.y, maxBounds.z);
		m_boundingBoxCorners[4] = new Vector3(maxBounds.x, minBounds.y, minBounds.z);
		m_boundingBoxCorners[5] = new Vector3(maxBounds.x, minBounds.y, maxBounds.z);
		m_boundingBoxCorners[6] = new Vector3(maxBounds.x, maxBounds.y, minBounds.z);
		m_boundingBoxCorners[7] = new Vector3(maxBounds.x, maxBounds.y, maxBounds.z);

		for(int i = 0; i < 8; i++)
			m_boundingBoxCorners[i] = transform.TransformPoint(m_boundingBoxCorners[i]);
	}
	
	private void DrawBoundingBox()
	{
		//set line widths:
		//-----------------
		m_lineWidth += (m_targetLineWidth - m_lineWidth) * (1.0f - Mathf.Pow(0.975f, 1000.0f * Time.deltaTime));

		for(int i = 0; i < m_lineRenderers.Length; i++)
		{
			m_lineRenderers[i].startWidth = m_lineWidth;
			m_lineRenderers[i].endWidth = m_lineWidth;
		}

		//set line positions:
		//-----------------
		int lineIndex = 0;
		
		SetLinePositions(lineIndex++, m_boundingBoxCorners[0], m_boundingBoxCorners[1]);
		SetLinePositions(lineIndex++, m_boundingBoxCorners[0], m_boundingBoxCorners[2]);
		SetLinePositions(lineIndex++, m_boundingBoxCorners[1], m_boundingBoxCorners[3]);
		SetLinePositions(lineIndex++, m_boundingBoxCorners[2], m_boundingBoxCorners[3]);
		
		SetLinePositions(lineIndex++, m_boundingBoxCorners[4], m_boundingBoxCorners[5]);
		SetLinePositions(lineIndex++, m_boundingBoxCorners[4], m_boundingBoxCorners[6]);
		SetLinePositions(lineIndex++, m_boundingBoxCorners[5], m_boundingBoxCorners[7]);
		SetLinePositions(lineIndex++, m_boundingBoxCorners[6], m_boundingBoxCorners[7]);
		
		SetLinePositions(lineIndex++, m_boundingBoxCorners[0], m_boundingBoxCorners[4]);
		SetLinePositions(lineIndex++, m_boundingBoxCorners[1], m_boundingBoxCorners[5]);
		SetLinePositions(lineIndex++, m_boundingBoxCorners[2], m_boundingBoxCorners[6]);
		SetLinePositions(lineIndex++, m_boundingBoxCorners[3], m_boundingBoxCorners[7]);
	}
	
	private void SetLinePositions(int index, Vector3 start, Vector3 end)
	{
		m_lineRenderers[index].SetPosition(0, start);
		m_lineRenderers[index].SetPosition(1, end);
	}
}
