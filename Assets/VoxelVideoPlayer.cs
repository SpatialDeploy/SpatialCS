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

[RequireComponent(typeof(Raytracer))]
public class VoxelVideoPlayer : MonoBehaviour
{
    private VoxelVideo m_video = null;
    private Raytracer m_raytracer = null;
    private VoxelVolume m_curVolume = null;

    private float m_startTime = 0.0f;
    private int m_curFrame = 0;

    //-------------------------//

    private void Start()
    {
        m_raytracer = GetComponent<Raytracer>();

        m_video = LoadVoxelVideo("Videos/cake");
        if(m_video == null)
        {
            Debug.LogWarning("Failed to load voxel video file");
            return;
        }

        m_startTime = Time.time;
        m_curFrame = 0;

        m_curVolume = Raytracer.CreateVolume(m_video.size, m_video.frames[m_curFrame]);
        m_raytracer.SetCurrentVolume(m_curVolume);
    }

    private void Update()
    {
        //skip if video wasnt loaded:
	    //-----------------
        if(m_video == null)
            return;

	    //update video frame if necessary:
	    //-----------------
        float curTime = Time.time - m_startTime;
        int frame = (int)(curTime * m_video.framerate);
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
