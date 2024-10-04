using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MaterialChange : MonoBehaviour
{
    // Blends between two materials
    Renderer renderer;
    public Material material1;

    public Material material2;

    float duration = 2.0f;

    void Start()
    {

        // At start, use the first material
        renderer = GetComponent<Renderer>();
        
        renderer.material = material1;

    }

    void Update()
    {

        // ping-pong between the materials over the duration

        float lerp = Mathf.PingPong(Time.time, duration) / duration;

        renderer.material.Lerp(material1, material2, lerp);

    }
}
