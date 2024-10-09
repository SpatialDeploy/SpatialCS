using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UIElements;

//-------------------------//

[RequireComponent(typeof(Camera))]
public class OrbitController : MonoBehaviour
{
    [SerializeField, Range(0.0f, 10.0f)]
    private float m_minRad = 1.5f;
    [SerializeField, Range(0.0f, 10.0f)]
    private float m_maxRad = 5.0f;
    [SerializeField, Range(0.0f, 10.0f)]
    private float m_sensitivity = 5.0f;
    [SerializeField, Range(0.0f, 10.0f)]
    private float m_scrollSensitivity = 3.0f;

    private float m_radius = 0.0f;
    private float m_theta = 45.0f;
    private float m_phi = 45.0f;

    //-------------------------//

    private void Awake()
    {
        m_radius = (m_minRad + m_maxRad) / 2.0f;
    }

    private void LateUpdate()
    {
        if(Input.GetMouseButton(1))
        {
            m_theta += Input.GetAxis("Mouse X") * m_sensitivity;
            m_phi -= Input.GetAxis("Mouse Y") * m_sensitivity;

            m_phi = Mathf.Clamp(m_phi, -89.0f, 89.0f);
        }

        m_radius -= Input.GetAxis("Mouse ScrollWheel") * m_scrollSensitivity;
        m_radius = Mathf.Clamp(m_radius, m_minRad, m_maxRad);

        Quaternion rotation = Quaternion.Euler(m_phi, m_theta, 0.0f);
        Vector3 position = rotation * new Vector3(0.0f, 0.0f, -m_radius);

        transform.SetPositionAndRotation(position, rotation);
    }
}
