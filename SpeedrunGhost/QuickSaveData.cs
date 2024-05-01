using System.Collections.Generic;
using UnityEngine;

namespace SpeedrunningTools;

public struct QuickSaveData
{
    public bool Valid;
    public bool RecordingValid;
    public float Time;

    public Vector3 CameraPosition;
    
    public Dictionary<string, Vector3> Positions;
    public Dictionary<string, float> Rotations;
    public Dictionary<string, Vector2> Velocities;
    public Dictionary<string, float> AngularVelocites;

    public Vector3[] TransformPositions;
    public Quaternion[] TransformRotations;
    
    public Dictionary<string, Vector2> LegOffsets;

    public Dictionary<string, bool> GrabState;
}