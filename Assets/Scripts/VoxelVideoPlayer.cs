using System;
using System.Collections;
using System.Collections.Generic;
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

    //-------------------------//

    private void Start()
    {
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

        m_video = LoadVoxelVideo("Videos/cake");
        if(m_video == null)
        {
            Debug.LogWarning("Failed to load voxel video file");
            return;
        }

        m_isPlaying = true;
        m_curTime = 0.0f;
        m_curFrame = 0;

        m_curVolume = Raytracer.CreateVolume(m_video.size, m_video.frames[m_curFrame]);
        m_raytracer.SetCurrentVolume(m_curVolume);
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

            m_curVolume = Raytracer.CreateVolume(m_video.size, m_video.frames[frame]);
            m_raytracer.SetCurrentVolume(m_curVolume);
            m_curFrame = frame;
        }
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

        TextAsset asset = Resources.Load<TextAsset>("Videos/cake");
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
}
