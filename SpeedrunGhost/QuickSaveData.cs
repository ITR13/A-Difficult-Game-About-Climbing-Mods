using UnityEngine;

namespace SpeedrunGhost;

public struct QuickSaveData
{
    public bool Valid;
    public float Time;
    
    public Vector3[] Positions;
    public float[] Rotations;
    public Vector2[] Velocities;
    public float[] AngularVelocites;
}