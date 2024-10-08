using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayheadControl : MonoBehaviour
{
	[SerializeField]
	private GameObject m_camera;
	//private VoxelVideoPlayer m_player;

	//-------------------------//

	private void Update()
	{
		if(m_camera == null)
		{
			float yBase = -0.5f - transform.localScale.y / 2.0f;
			float zBase =  0.5f + transform.localScale.z / 2.0f;
			transform.localPosition = new Vector3(0.0f, yBase, zBase);
			return;
		}

		Vector3 dist = m_camera.transform.position - transform.parent.position;
		dist.y = 0.0f;
		dist.Normalize();

		float angle = Vector3.Angle(dist, new Vector3(0.0f, 0.0f, 1.0f));
		if(dist.x < 0.0f)
			angle = 360.0f - angle;
		angle -= transform.parent.eulerAngles.y;
		angle += 45.0f;
		angle -= angle % 90.0f;

		float x = Mathf.Sin(angle * Mathf.Deg2Rad);
		float y = -0.5f - transform.localScale.y / 2.0f;
		float z = Mathf.Cos(angle * Mathf.Deg2Rad);

		transform.localPosition = new Vector3(x, y, z);
		transform.localEulerAngles = new Vector3(0.0f, angle, 0.0f);
	}

	private void OnMouseDown()
	{
		Debug.Log("clicked");
	}
}
