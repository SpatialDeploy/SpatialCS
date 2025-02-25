using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using SPLVnative;
using System.Diagnostics;

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

// used for handling the background decoder thread
public class SpatialCSdecodingThreadData
{
	public bool active;
	public Thread thread;
	public uint frameIdx;
	public IntPtr decodedFrame;
	public SpatialCSdecoder decoder;
}

//-------------------------//

//wrapper over SPLVdecoder to easily interface with C#
public class SpatialCSdecoder
{
	private IntPtr m_decoder;
	private List<SPLVframeIndexed> m_decodedFrames;

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
		m_decodedFrames = new List<SPLVframeIndexed>();

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

		foreach(var frame in m_decodedFrames)
		{
			SPLV.FrameDestroy(frame.frame);
			Marshal.FreeHGlobal(frame.frame);
		}
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
			throw new Exception("frame index out of bounds");

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

			SPLV.FrameCompactDestroy(m_decodingThreadData.decodedFrame);
			Marshal.FreeHGlobal(m_decodingThreadData.decodedFrame);
		}

		//start decoding thread:
		//-----------------
		m_decodingThreadData.frameIdx = idx;
		m_decodingThreadData.active = true;
		m_decodingThreadData.thread = new Thread(StartDecodingThread);
		m_decodingThreadData.thread.Start(m_decodingThreadData);
	}

	public bool TryGetDecodedFrame(out IntPtr frame)
	{
		frame = IntPtr.Zero;

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

	public void FreeFrame(IntPtr frame)
	{
		SPLV.FrameCompactDestroy(m_decodingThreadData.decodedFrame);
		Marshal.FreeHGlobal(m_decodingThreadData.decodedFrame);
	}

	//-------------------------//

	private IntPtr DecodeFrame(uint frameIdx)
	{
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

				UnityEngine.Debug.LogWarning("decoding extra frame");
				DecodeFrame((uint)depIdx);
			}

			Marshal.FreeHGlobal(dependenciesRecursive);
		}

		//decode:
		//-----------------
		IntPtr dependencies = Marshal.AllocHGlobal((int)numDependencies * Marshal.SizeOf<SPLVframeIndexed>());
		for(uint i = 0; i < numDependencies; i++)
		{
			//TODO: simplify

			ulong depIdx = (ulong)Marshal.ReadInt64(dependencyIndices, (int)(i * sizeof(ulong)));

			SPLVframeIndexed dependencyStruct = Marshal.PtrToStructure<SPLVframeIndexed>(dependencies + (int)(i * Marshal.SizeOf<SPLVframeIndexed>()));
			dependencyStruct.index = depIdx;
			dependencyStruct.frame = m_decodedFrames[(int)SearchDecodedFrames(depIdx)].frame;

			Marshal.StructureToPtr(dependencyStruct, dependencies + (int)(i * Marshal.SizeOf<SPLVframeIndexed>()), false);
		}

		IntPtr frame = Marshal.AllocHGlobal(Marshal.SizeOf<SPLVframe>());
		IntPtr frameCompact = Marshal.AllocHGlobal(Marshal.SizeOf<SPLVframeCompact>());

		var decodeError = SPLV.DecoderDecodeFrame(m_decoder, frameIdx, numDependencies, dependencies, frame, frameCompact);
		if(decodeError != SPLVerror.SUCCESS)
		{
			Marshal.FreeHGlobal(dependencyIndices);
			Marshal.FreeHGlobal(dependencies);
			Marshal.FreeHGlobal(frame);

			throw new Exception($"failed to decode frame with error ({decodeError})");
		}

		//free frames which are no longer dependencies:
		//-----------------
		for(int i = m_decodedFrames.Count - 1; i >= 0; i--)
		{
			bool found = false;
			for(uint j = 0; j < numDependencies; j++)
			{
				ulong depIdx = (ulong)Marshal.ReadInt64(dependencyIndices, (int)(j * sizeof(ulong)));
				if(m_decodedFrames[i].index == depIdx)
				{
					found = true;
					break;
				}
			}

			if(!found)
			{
				SPLV.FrameDestroy(m_decodedFrames[i].frame);
				Marshal.FreeHGlobal(m_decodedFrames[i].frame);

				m_decodedFrames.RemoveAt(i);
			}
		}

		m_decodedFrames.Add(new SPLVframeIndexed{
			frame = frame,
			index = frameIdx
		});

		//cleanup + return:
		//-----------------
		Marshal.FreeHGlobal(dependencyIndices);
		Marshal.FreeHGlobal(dependencies);

		return frameCompact;
	}

	private static void StartDecodingThread(object arg)
	{
		var data = (SpatialCSdecodingThreadData)arg;

        Stopwatch stopwatch = new Stopwatch();
        stopwatch.Start();

		data.decodedFrame = data.decoder.DecodeFrame(data.frameIdx);

        stopwatch.Stop();
		UnityEngine.Debug.Log($"decoding took {(float)stopwatch.ElapsedTicks / (float)Stopwatch.Frequency * 1000.0f}ms");
	}

	//-------------------------//
	
	private long SearchDecodedFrames(ulong frameIdx)
	{
		for(int i = 0; i < m_decodedFrames.Count; i++)
		{
			if(m_decodedFrames[i].index == frameIdx)
				return i;
		}

		return -1;
	}
}