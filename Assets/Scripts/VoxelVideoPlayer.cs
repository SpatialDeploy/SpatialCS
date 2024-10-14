using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Defective.JSON;
using UnityEngine;

//-------------------------//

public class VoxelVideo
{
    public int version;
    public string title;

    public float framerate;
    public int numFrames;
    public float duration;
    
    public Vector3Int size;
    public string[][] frames;
};

//-------------------------//

public class VoxelVideoPlayer : MonoBehaviour
{
    private VoxelVideo m_video = null;
    private Raytracer m_raytracer = null;
    private VoxelVolume m_curVolume = null;

    private const float BOUNDING_BOX_WIDTH = 0.005f;
    private readonly Vector3[] m_boundingBoxCorners = new Vector3[8];
    private readonly LineRenderer[] m_lineRenderers = new LineRenderer[12];

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
        //get raytracer:
	    //-----------------
        Camera camera = Camera.main;
        if(camera == null)
        {
            Debug.LogWarning("Failed to find main camera");
            return;
        }

        m_raytracer = camera.GetComponent<Raytracer>();
        if(m_raytracer == null)
        {
            Debug.LogWarning("Main camera does not have Raytracer behavior");
            return;
        }

        //load video:
	    //-----------------
        m_video = LoadVoxelVideo("Videos/Coates");
        if(m_video == null)
        {
            Debug.LogWarning("Failed to load voxel video file");
            return;
        }

        m_video.size = new Vector3Int(125, 125, 125); //TEMP!!!

        //setup video playback params:
	    //-----------------
        m_isPlaying = true;
        m_curTime = 0.0f;
        m_curFrame = 0;

        m_curVolume = Raytracer.CreateVolume(m_video.size, File.ReadAllBytes("Assets/Resources/Videos/out.bin")); //TEMP!!!
        m_raytracer.SetCurrentVolume(m_curVolume);

        //setup bounding box rendering:
	    //-----------------
        SetupBoundingBox();
    }

    private void Update()
    {
        //skip if video wasnt loaded or raytracer wasnt found:
	    //-----------------
        if(m_video == null || m_raytracer == null)
            return;

	    //update video frame if necessary:
	    //-----------------
        if(m_isPlaying && !m_adjustingProgress)
            m_curTime += Time.deltaTime;

        int frame = (int)(m_curTime * m_video.framerate);
        frame %= m_video.numFrames;

        if(m_curFrame != frame)
        {
            if(m_curVolume != null)
                Raytracer.DestroyVolume(m_curVolume);

            m_curVolume = Raytracer.CreateVolume(m_video.size, File.ReadAllBytes("Assets/Resources/Videos/out.bin")); //TEMP!!!
            m_raytracer.SetCurrentVolume(m_curVolume);
            m_curFrame = frame;
        }

	    //draw bounding box:
	    //-----------------
        DrawBoundingBox();
    }

    private void OnDestroy()
    {
        m_raytracer.SetCurrentVolume(null);

        if(m_curVolume != null)
            Raytracer.DestroyVolume(m_curVolume);
    }

    private VoxelVideo LoadVoxelVideo(string name)
    {
        //load into json object:
        //-----------------	
        VoxelVideo video = new VoxelVideo();

        TextAsset asset = Resources.Load<TextAsset>(name);
        JSONObject videoObject = new JSONObject(asset.ToString());

        //ensure all top-level fields exist:
        //-----------------	
        if(!videoObject.HasField("Title")      ||
           !videoObject.HasField("Version")    ||
           !videoObject.HasField("Framerate")  ||
           !videoObject.HasField("Framecount") ||
           !videoObject.HasField("Duration")   ||
           !videoObject.HasField("Dimensions") ||
           !videoObject.HasField("Blocks"))
        {
            return null;
        }

        //load top-level fields:
        //-----------------	
        video.title = videoObject.GetField("Title").stringValue;
        video.version = videoObject.GetField("Version").intValue;
        video.framerate = videoObject.GetField("Framerate").floatValue;
        video.numFrames = videoObject.GetField("Framecount").intValue;
        video.duration = videoObject.GetField("Duration").floatValue;

        //load dimensions:
        //-----------------	
        JSONObject dimObject = videoObject.GetField("Dimensions");
        if(!dimObject.HasField("x") || !dimObject.HasField("y") || !dimObject.HasField("z"))
            return null;
        video.size = new Vector3Int(
            dimObject.GetField("x").intValue,
            dimObject.GetField("y").intValue,
            dimObject.GetField("z").intValue
        );
        
        //load blocks:
        //-----------------	
        JSONObject blocksObject = videoObject.GetField("Blocks");
        if(blocksObject.list.Count != video.numFrames)
            return null;

        int voxelsPerFrame = video.size.x * video.size.y * video.size.z;

        video.frames = new string[video.numFrames][];
        for(int i = 0; i < video.numFrames; i++)
        {
            JSONObject frameObject = blocksObject.list[i];
            if(frameObject.list.Count < voxelsPerFrame)
                return null;

            video.frames[i] = new string[voxelsPerFrame];
            for(int j = 0; j < voxelsPerFrame; j++)
                video.frames[i][j] = frameObject.list[j].stringValue;
        }

        return video;
    }
    private void SetupBoundingBox()
    {
        //create line renderers:
	    //-----------------
        GameObject linesParent = new GameObject("BoundingBoxLines");
        linesParent.transform.SetParent(transform);
        for(int i = 0; i < 12; i++)
        {
            GameObject lineObj = new GameObject($"Line_{i}");
            lineObj.transform.SetParent(linesParent.transform);
            
            LineRenderer line = lineObj.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.startWidth = BOUNDING_BOX_WIDTH;
            line.endWidth = BOUNDING_BOX_WIDTH;
            line.material = new Material(Shader.Find("Sprites/Default"));
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
