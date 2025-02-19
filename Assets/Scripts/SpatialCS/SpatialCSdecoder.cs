using UnityEngine;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using SPLVnative;

//-------------------------//

// metadata for a spatial
public struct SpatialCSmetadata
{
	public uint width;
	public uint height;
	public uint depth;
	public float framerate;
	public uint framecount;
	public float duration;
}

// reference to a frame, used for memory management
public class SpatialCSframeRef
{
	public IntPtr frame;
	public ulong frameIdx;
	public int refCount;
}

// used for handling the background decoder thread
public class SpatialCSdecodingThreadData
{
	public bool active;
	public Thread thread;
	public uint frameIdx;
	public SpatialCSframeRef decodedFrame;
	public SpatialCSdecoder decoder;
}

//-------------------------//

//wrapper over SPLVdecoder to easily interface with C#
public class SpatialCSdecoder
{
	private IntPtr m_decoder;
	private List<SpatialCSframeRef> m_decodedFrames;

	private SpatialCSmetadata m_metadata;
	
	private SpatialCSdecodingThreadData m_decodingThreadData;

	//-------------------------//

	public SpatialCSdecoder(string spatialPath)
	{
		//create decoder:
		//-----------------	
		IntPtr unmanagedPath = Marshal.StringToHGlobalAnsi(spatialPath);

		m_decoder = Marshal.AllocHGlobal(Marshal.SizeOf<SPLVdecoder>());
		SPLVerror decoderCreateError = SPLV.DecoderCreateFromFile(m_decoder, unmanagedPath);
		if(decoderCreateError != SPLVerror.SUCCESS)
		{
			Marshal.FreeHGlobal(m_decoder);
			m_decoder = IntPtr.Zero;

			throw new Exception($"failed to create SPLVdecoder with error ({decoderCreateError})");
		}

		//initialize fields:
		//-----------------	
		m_decodedFrames = new List<SpatialCSframeRef>();

		SPLVdecoder decoderStruct = Marshal.PtrToStructure<SPLVdecoder>(m_decoder);
		m_metadata.width      = decoderStruct.width;
		m_metadata.height     = decoderStruct.height;
		m_metadata.depth      = decoderStruct.depth;
		m_metadata.framerate  = decoderStruct.framerate;
		m_metadata.framecount = decoderStruct.frameCount;
		m_metadata.duration   = decoderStruct.duration;

		//init decoding frame data:
		//-----------------
		m_decodingThreadData = new SpatialCSdecodingThreadData
		{
			active = false,
			decoder = this
		};

		//cleanup:
		//-----------------
		Marshal.FreeHGlobal(unmanagedPath);	
	}

	~SpatialCSdecoder()
	{
		if(m_decoder != IntPtr.Zero)
		{
			SPLV.DecoderDestroy(m_decoder);
			Marshal.FreeHGlobal(m_decoder);
		}

		//TODO: what if something is still using one of the frames?
		foreach(var frame in m_decodedFrames)
			FrameRefRemove(frame);
		m_decodedFrames.Clear();

		if(m_decodingThreadData != null && m_decodingThreadData.active)
		{
			m_decodingThreadData.thread.Join();
			m_decodingThreadData.active = false;
		}
	}

	//-------------------------//

	public SpatialCSmetadata GetMetadata()
	{
		return m_metadata;
	}

	public uint GetClosestDecodableFrameIdx(uint targetFrameIdx)
	{
		//validate:
		//-----------------
		if(targetFrameIdx >= m_metadata.framecount)
			throw new System.Exception("frame index out of bounds");

		//loop and check all previous frames:
		//-----------------
		long frameIdx = targetFrameIdx;
		while (true)
		{
			if(frameIdx < 0)
				break;

			ulong numDependencies;
			SPLV.DecoderGetFrameDependencies(m_decoder, (ulong)frameIdx, out numDependencies, IntPtr.Zero, 0);

			if(numDependencies == 0)
				break;

			IntPtr dependencies = Marshal.AllocHGlobal((int)(numDependencies * sizeof(ulong)));
			SPLV.DecoderGetFrameDependencies(m_decoder, (ulong)frameIdx, out numDependencies, dependencies, 0);

			bool found = true;
			for(uint i = 0; i < numDependencies; i++)
			{
				ulong depIdx = (ulong)Marshal.ReadInt64(dependencies, (int)(i * sizeof(ulong)));
				if(SearchDecodedFrames(depIdx) < 0)
				{
					found = false;
					break;
				}
			}

			Marshal.FreeHGlobal(dependencies);

			if(found)
				break;

			frameIdx--;
		}

		//return:
		//-----------------
		if (frameIdx < 0)
			throw new Exception("o decodable frame found");

		return (uint)frameIdx;
	}

	public void StartDecodingFrame(uint idx)
	{
		//validate:
		//-----------------
		if(idx >= m_metadata.framecount)
			throw new Exception("frame index out of bounds");

		//finish decoding previous frame:
		//-----------------
		if(m_decodingThreadData.active)
		{
			m_decodingThreadData.thread.Join();
			m_decodingThreadData.active = false;
			FrameRefRemove(m_decodingThreadData.decodedFrame);
		}

		//start decoding thread:
		//-----------------
		m_decodingThreadData.frameIdx = idx;
		m_decodingThreadData.active = true;
		m_decodingThreadData.thread = new Thread(StartDecodingThread);
		m_decodingThreadData.thread.Start(m_decodingThreadData);
	}

	public bool TryGetDecodedFrame(out SpatialCSframeRef frame)
	{
		frame = null;

		//ensure thread is active:
		//-----------------
		if(!m_decodingThreadData.active)
			throw new Exception("no frame is being decoded");

		//check if decoding finished:
		//-----------------
		if(m_decodingThreadData.thread.IsAlive)
			return false;

		m_decodingThreadData.active = false;
		frame = m_decodingThreadData.decodedFrame;
		return true;
	}

	public void FreeFrame(SpatialCSframeRef frame)
	{
		FrameRefRemove(frame);
	}

	//-------------------------//

	private SpatialCSframeRef DecodeFrame(uint frameIdx)
	{
		//check if frame was already decoded:
		//-----------------
		long searchResult = SearchDecodedFrames(frameIdx);
		if(searchResult >= 0)
			return m_decodedFrames[(int)searchResult];

		//check if dependencies were already decoded:
		//-----------------
		ulong numDependencies;
		SPLV.DecoderGetFrameDependencies(m_decoder, frameIdx, out numDependencies, IntPtr.Zero, 0);

		IntPtr dependencyIndices = IntPtr.Zero;
		if(numDependencies > 0)
		{
			dependencyIndices = Marshal.AllocHGlobal((int)(numDependencies * sizeof(ulong)));
			SPLV.DecoderGetFrameDependencies(m_decoder, frameIdx, out numDependencies, dependencyIndices, 0);
		}

		bool hasDeps = true;
		for(uint i = 0; i < numDependencies; i++)
		{
			ulong depIdx = (ulong)Marshal.ReadInt64(dependencyIndices, (int)(i * sizeof(ulong)));
			long depSearchResult = SearchDecodedFrames(depIdx);
			if(depSearchResult < 0)
			{
				hasDeps = false;
				break;
			}
		}

		if(!hasDeps)
		{
			ulong numDependenciesRecursive;
			SPLV.DecoderGetFrameDependencies(m_decoder, frameIdx, out numDependenciesRecursive, IntPtr.Zero, 1);

			IntPtr dependenciesRecursive = Marshal.AllocHGlobal((int)(numDependenciesRecursive * sizeof(ulong)));
			SPLV.DecoderGetFrameDependencies(m_decoder, frameIdx, out numDependenciesRecursive, dependenciesRecursive, 1);

			for(uint i = 0; i < numDependenciesRecursive; i++)
			{
				ulong depIdx = (ulong)Marshal.ReadInt64(dependenciesRecursive, (int)(i * sizeof(ulong)));

				Debug.LogWarning("decoding extra frame");
				DecodeFrame((uint)depIdx);
			}

			Marshal.FreeHGlobal(dependenciesRecursive);
		}

		//decode:
		//-----------------
		IntPtr dependencies = Marshal.AllocHGlobal((int)numDependencies * Marshal.SizeOf<SPLVframeIndexed>());
		for(uint i = 0; i < numDependencies; i++)
		{
			ulong depIdx = (ulong)Marshal.ReadInt64(dependencyIndices, (int)(i * sizeof(ulong)));

			SPLVframeIndexed dependencyStruct = Marshal.PtrToStructure<SPLVframeIndexed>(dependencies + (int)(i * Marshal.SizeOf<SPLVframeIndexed>()));
			dependencyStruct.index = depIdx;
			dependencyStruct.frame =  m_decodedFrames[(int)SearchDecodedFrames(depIdx)].frame;

			Marshal.StructureToPtr(dependencyStruct, dependencies + (int)(i * Marshal.SizeOf<SPLVframeIndexed>()), false);
		}

		IntPtr frame = Marshal.AllocHGlobal(Marshal.SizeOf<SPLVframe>());

		var decodeError = SPLV.DecoderDecodeFrame(m_decoder, frameIdx, numDependencies, dependencies, frame);
		if(decodeError != SPLVerror.SUCCESS)
		{
			Marshal.FreeHGlobal(dependencyIndices);
			Marshal.FreeHGlobal(dependencies);
			Marshal.FreeHGlobal(frame);

			throw new Exception($"failed to decode frame with error ({decodeError})");
		}

		//create frame ref:
		//-----------------
		var frameRef = new SpatialCSframeRef
		{
			frame = frame,
			frameIdx = frameIdx,
			refCount = 0
		};

		//free frames which are no longer dependencies:
		//-----------------
		for(int i = m_decodedFrames.Count - 1; i >= 0; i--)
		{
			bool found = false;
			for(uint j = 0; j < numDependencies; j++)
			{
				ulong depIdx = (ulong)Marshal.ReadInt64(dependencyIndices, (int)(j * sizeof(ulong)));
				if(m_decodedFrames[i].frameIdx == depIdx)
				{
					found = true;
					break;
				}
			}

			if(!found)
			{
				FrameRefRemove(m_decodedFrames[i]);
				m_decodedFrames.RemoveAt(i);
			}
		}

		m_decodedFrames.Add(FrameRefAdd(frameRef));

		//cleanup + return:
		//-----------------
		Marshal.FreeHGlobal(dependencyIndices);
		Marshal.FreeHGlobal(dependencies);

		return frameRef;
	}

	private static void StartDecodingThread(object arg)
	{
		var data = (SpatialCSdecodingThreadData)arg;

		//var startTime = Time.realtimeSinceStartup;

		data.decodedFrame = data.decoder.FrameRefAdd(
			data.decoder.DecodeFrame(data.frameIdx)
		);

		//var duration = (Time.realtimeSinceStartup - startTime) * 1000f;
		//Debug.Log($"decoding took {duration}ms");
	}

	//-------------------------//
	
	private long SearchDecodedFrames(ulong frameIdx)
	{
		for(int i = 0; i < m_decodedFrames.Count; i++)
		{
			if(m_decodedFrames[i].frameIdx == frameIdx)
				return i;
		}

		return -1;
	}

	//-------------------------//

	private void FrameRefRemove(SpatialCSframeRef ref_)
	{
		ref_.refCount--;
		if(ref_.refCount <= 0)
		{
			SPLV.FrameDestroy(ref_.frame);
			Marshal.FreeHGlobal(ref_.frame);
		}
	}

	private SpatialCSframeRef FrameRefAdd(SpatialCSframeRef ref_)
	{
		ref_.refCount++;
		return ref_;
	}
}