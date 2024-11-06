using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.IO;
using System.Linq;
using Defective.JSON;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.VFX;

//-------------------------//

public class VoxelVideo
{
    public Vector3Int size;
    public float framerate;
    public int framecount;
    public float duration;
    
    public byte[][] frames;
};

//-------------------------//

public class VoxelVideoPlayer : MonoBehaviour
{
    [SerializeField]
    private VolumeRendererFeature m_renderFeature;
    private VoxelVideo m_video = null;
    private VoxelVolume m_curVolume = null;

    [SerializeField]
    private Material m_boundingBoxMaterial = null;
    private const float BOUNDING_BOX_WIDTH = 0.005f;
    private readonly Vector3[] m_boundingBoxCorners = new Vector3[8];
    private readonly LineRenderer[] m_lineRenderers = new LineRenderer[12];
    private float m_targetLineWidth = 0.0f;
    private float m_lineWidth = 0.0f;

    private float m_curTime = 0.0f;
    private int m_curFrame = 0;

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
        if(m_video == null)
            return;

        m_curTime = val * m_video.duration;
    }

    public void SetAdjustingProgess(bool adjusting)
    {
        m_adjustingProgress = adjusting;
    }

    public float GetProgress()
    {
        if(m_video == null)
            return 0.0f;

        return (m_curTime / m_video.duration) % 1.0f;
    }

    public Vector3Int GetVideoResolution()
    {
        if(m_video == null)
            return new Vector3Int(1, 1, 1);

        return new Vector3Int(m_video.size.x, m_video.size.z, m_video.size.y);
    }

    //-------------------------//

    private void Start()
    {
        //load video:
	    //-----------------
        m_video = LoadVoxelVideo("Videos/mandelbulb_hihires");
        if(m_video == null)
        {
            Debug.LogWarning("Failed to load voxel video file");
            return;
        }

        //setup video playback params:
	    //-----------------
        m_isPlaying = true;
        m_curTime = 0.0f;
        m_curFrame = -1;
        m_curVolume = null;

        //setup bounding box rendering:
	    //-----------------
        SetupBoundingBox();
    }

    private void Update()
    {
        //skip if video wasnt loaded or raytracer wasnt found:
	    //-----------------
        if(m_video == null || m_renderFeature == null)
            return;

	    //update video frame if necessary:
	    //-----------------
        if(m_isPlaying && !m_adjustingProgress)
            m_curTime += Time.deltaTime;

        int frame = (int)(m_curTime * m_video.framerate);
        frame %= m_video.framecount;

        if(m_curFrame != frame)
        {
            if(m_curVolume != null)
                VolumeRendererFeature.DestroyVolume(m_curVolume);

            m_curVolume = VolumeRendererFeature.CreateVolume(m_video.size, m_video.frames[frame]);
            m_renderFeature.SetCurrentVolume(m_curVolume);
            m_curFrame = frame;
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
            VolumeRendererFeature.DestroyVolume(m_curVolume);
    }

    public void OnHover()
    {
        m_targetLineWidth = BOUNDING_BOX_WIDTH;
    }

    public void OnUnHover()
    {
        m_targetLineWidth = 0.0f;
    }

    private VoxelVideo LoadVoxelVideo(string name)
    {
        //load into byte array and create binary reader:
        //-----------------	
        byte[] rawData = Resources.Load<TextAsset>(name).bytes;

        MemoryStream memoryStream = new MemoryStream(rawData);
        BinaryReader reader = new BinaryReader(memoryStream);

        //read metadata:
        //-----------------	
        uint width = reader.ReadUInt32();
        uint height = reader.ReadUInt32();
        uint depth = reader.ReadUInt32();
        float framerate = reader.ReadSingle();
        uint framecount = reader.ReadUInt32();
        float duration = reader.ReadSingle();

        //copy each frame into frame array:
        //-----------------	
        byte[][] frames = new byte[framecount][];
        
        for(uint i = 0; i < framecount; i++)
        {
            uint frameSize = reader.ReadUInt32();
            frames[i] = reader.ReadBytes((int)frameSize);
        }

        //create video struct and return:
        //-----------------	
        VoxelVideo video = new()
        {
            size = new Vector3Int((int)width, (int)height, (int)depth),
            framerate = framerate,
            framecount = (int)framecount,
            duration = duration,
            frames = frames
        };

        return video;
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
        Vector3Int videoSize = GetVideoResolution();
		int maxSize = Math.Max(Math.Max(videoSize.x, videoSize.y), videoSize.z);

        Vector3 minBounds = new Vector3(-(float)videoSize.x / maxSize / 2.0f, -(float)videoSize.y / maxSize / 2.0f, -(float)videoSize.z / maxSize / 2.0f);
        Vector3 maxBounds = new Vector3( (float)videoSize.x / maxSize / 2.0f,  (float)videoSize.y / maxSize / 2.0f,  (float)videoSize.z / maxSize / 2.0f);

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
