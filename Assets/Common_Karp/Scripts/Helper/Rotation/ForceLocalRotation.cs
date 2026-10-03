using System.Collections;
using System.Collections.Generic;
using UnityEngine;


[ExecuteAlways]
public class ForceLocalRotation : MonoBehaviour
{
    public Quaternion rotation = Quaternion.identity;

    void Update()
    {
        transform.rotation = rotation;
    }
}

