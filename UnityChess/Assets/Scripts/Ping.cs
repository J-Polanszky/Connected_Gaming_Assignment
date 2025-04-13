using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Ping : MonoBehaviourSingleton<Ping>
{
    private void Awake()
    {
        if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        DontDestroyOnLoad(gameObject);
    }
}
