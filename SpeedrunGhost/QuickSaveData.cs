using UnityEngine;

namespace SpeedrunningTools;

public struct QuickSaveData
{
    public bool Valid;
    public float Time;
    
    public Vector3[] Positions;
    public float[] Rotations;
    public Vector2[] Velocities;
    public float[] AngularVelocites;

    public Vector3[] TransformPositions;
    public Quaternion[] TransformRotations;
    
    public Vector2[] LegOffsets;
}